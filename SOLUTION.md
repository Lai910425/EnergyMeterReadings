Solution – Energy Meter Readings API
1. Overview
This project implements a RESTful Web API for managing energy meter readings per site. It allows clients to create and list sites, meters and readings, filter readings by time range, and calculate energy consumption for a meter over a date range, with a daily breakdown.
Readings are cumulative kWh values. Consumption is derived from the differences between readings over time. All timestamps are UTC (ISO 8601).
2. Technology Stack
•	C# 12
•	.NET 8
•	ASP.NET Core Web API (controllers)
•	Entity Framework Core 8 (code-first, one initial migration)
•	Microsoft SQL Server 2022 (Developer Edition in Docker for local setup)
•	xUnit, with in-memory SQLite for the database-behaviour tests
•	Swagger / OpenAPI for exploring the API
3. Architecture and Design Decisions
Controllers rather than Minimal APIs
I chose controllers for these reasons:
•	[ApiController] gives automatic 400 Bad Request responses with ValidationProblemDetails, driven by data annotations and IValidatableObject. With Minimal APIs on .NET 8 I'd have to write that validation wiring myself.
•	The API has three clear resources (sites, meters, readings). Three small controllers map directly onto them, which makes the API surface easy to scan and explain.
•	The trade-off is a little more ceremony and reflection-based startup than Minimal APIs. At this scope that cost doesn't matter.
Project structure
The solution has one API project and one test project. Inside the API, responsibilities are separated by folder:
src/EnergyMeter.Api
  Controllers/      HTTP surface: routing, binding, status codes
  Contracts/        Request/response DTOs and input validation
  Services/         Application logic: existence checks, duplicate rule, EF Core queries
  Domain/           Entities, ConsumptionCalculator (pure logic), domain exceptions
  Data/             DbContext, InitialCreate migration, demo data seeder
  Infrastructure/   UTC timestamp handling, model binder, exception → problem details mapping
tests/EnergyMeter.Tests
database/           Idempotent SQL scripts (schema + optional demo data)
A request flows like this:
Controller → Service → ConsumptionCalculator (pure) / EnergyMeterDbContext (SQL Server)
•	Controllers stay thin. They validate input, call a service and return the right status code.
•	Services own the business rules that need the database.
•	ConsumptionCalculator is a pure static function with no database, clock or I/O, so the core logic can be unit tested quickly and exhaustively.
•	Domain/ has no dependency on ASP.NET Core or EF Core.
At this size, separate Domain, Application and Infrastructure projects would add ceremony without adding clarity. The folder boundaries are drawn so they could become projects later.
Resource design
Method	Route	Purpose
POST / GET	/api/sites	Create / list sites
GET	/api/sites/{siteId}	Get a site
POST / GET	/api/sites/{siteId}/meters	Create / list meters at a site
GET	/api/meters/{meterId}	Get a meter
POST / GET	/api/meters/{meterId}/readings?fromUtc=&toUtc=	Record / list readings (filters optional)
GET	/api/meters/{meterId}/summary?fromUtc=&toUtc=	Consumption summary
•	Meters are created and listed under their site, because a meter can't exist without one. Once a meter exists it is addressed directly (/api/meters/{id}), which avoids deeply nested URLs for readings and summaries.
•	The summary is a read-only computed view, so it is a GET, not a stored resource.
Timestamps
All timestamps are UTC, and the API never guesses:
•	Input must include Z or an offset. 2026-11-01T10:00:00+02:00 is accepted and stored as 2026-11-01T08:00:00Z. A timestamp with no zone is rejected with 400 rather than assumed to be UTC or server-local time, because silently misreading a timestamp would corrupt consumption figures.
•	The same rule applies to JSON bodies (UtcDateTimeJsonConverter) and query strings (UtcDateTimeModelBinder). Both use one helper (UtcTimestamp), so the two paths can't drift apart.
•	Output is always ISO 8601 UTC with a trailing Z.
4. Database Design
Relationships
•	One Site can have multiple Meters (Meters.SiteId → Sites.Id, cascade delete).
•	One Meter can have multiple Readings (Readings.MeterId → Meters.Id, cascade delete).
Table	Columns
Sites	Id int identity PK, Name nvarchar(200), CreatedUtc datetime2
Meters	Id int identity PK, SiteId int FK, SerialNumber nvarchar(100), CreatedUtc datetime2
Readings	Id bigint identity PK, MeterId int FK, TimestampUtc datetime2, ValueKwh decimal(18,3)
Design choices:
•	ValueKwh is decimal(18,3). That gives exact arithmetic with no floating-point drift when summing, and watt-hour resolution. Values with more than three decimals are rejected rather than rounded silently by SQL Server.
•	Readings.Id is bigint because readings are by far the highest-volume table.
•	datetime2 stores no time-zone information, so an EF Core value converter marks every value read back as DateTimeKind.Utc.
How EF Core manages the database
•	The model is defined code-first in EnergyMeterDbContext.OnModelCreating.
•	A single migration, InitialCreate, creates the whole schema.
•	In Development the app applies the migration and seeds a small demo data set on startup, so a reviewer can run it immediately. Both behaviours are controlled by configuration (Database:ApplyMigrationsOnStartup, Database:SeedDemoData) and are off outside Development. In production, migrations should be applied deliberately, not by the app.
•	database/01_create_schema.sql contains the same schema as an idempotent script for running in SSMS. It also records the migration in __EFMigrationsHistory, so EF and the script agree on the database state.
•	The connection string is configurable through ConnectionStrings:EnergyMeterDb in appsettings.json, or the ConnectionStrings__EnergyMeterDb environment variable. EnableRetryOnFailure covers the SQL Server container still starting up and transient network errors.
Preventing duplicate reading timestamps
The rule is that, for a given meter, each reading timestamp must be unique. It is enforced in two layers that produce the same outcome:
1.	Unique constraint (source of truth). A unique index on (MeterId, TimestampUtc) means the database itself refuses a duplicate, whatever code path inserts it. The same index also serves every reading query, because they all filter by meter and time range.
2.	Service pre-check. Before inserting, ReadingService checks whether the meter already has a reading at that timestamp. If it does, it returns a clean 409 Conflict.
If two requests arrive at the same moment and both pass the pre-check, the unique index rejects the second insert. The service catches the DbUpdateException, confirms the row now exists and throws the same DuplicateReadingException, so the client always gets the same 409. The service confirms the duplicate by checking the data rather than matching SQL Server error numbers (2601/2627), so the same code path works on any provider, including SQLite in the tests.
Because timestamps are normalised to UTC first, 08:00:00Z and 10:00:00+02:00 are correctly treated as the same instant, and therefore as duplicates.
5. Consumption Calculation
The summary takes fromUtc and toUtc and uses only the readings whose timestamps fall within the inclusive range [fromUtc, toUtc], ordered by timestamp.
•	Total consumption is the latest reading minus the earliest reading in the range.
•	If fewer than two readings exist in the range, total consumption is zero and the daily breakdown is empty.
•	Daily consumption is calculated from consecutive readings. Each difference is attributed to the UTC date of the later reading in the pair, and only positive differences are summed per day. A pair that spans midnight therefore counts towards the next day. Days with no pair attributed to them are omitted.
•	With normal increasing data, the daily totals add up to the total consumption.
The logic lives in ConsumptionCalculator.Calculate(readings, fromUtc, toUtc), a pure function. It applies the range filter and ordering itself, so the result is correct whatever order the caller passes readings in. The service filters by range in SQL first, so only the relevant rows are loaded, then calls the calculator.
Meter resets (optional, implemented)
A reading that is lower than the previous one is treated as a meter reset or rollover:
•	That negative difference contributes nothing to any day, which follows directly from "sum only positive differences".
•	When a decrease is present, "latest minus earliest" would give a misleading or even negative total. In that case only, the total becomes the sum of positive differences, which keeps total = sum of daily values. For normal increasing data both formulas give the same number, so the brief's definition still holds.
•	The response includes decreaseDetected: true so a client can flag that period for review.
Example: readings of 100 → 150 → 20 (reset) → 50 give a total of 80 kWh (50 + 30), not −50.
6. Validation and Error Handling
All errors use the RFC 7807 problem details format, so clients always receive a consistent JSON shape.
Situation	Response
Missing or invalid fields (empty name, negative kWh, more than 3 decimals, malformed JSON)	400 with field-level errors
Timestamp without Z or an offset	400, with a message showing the expected format
fromUtc later than toUtc	400
fromUtc / toUtc missing on the summary	400
Site or meter does not exist	404
Reading timestamp already exists for that meter	409, including the meterId and timestampUtc
Anything unexpected	500, with no internal details leaked
•	Invalid requests are caught by data annotations and IValidatableObject on the request DTOs, which [ApiController] turns into 400 responses automatically.
•	Missing records: the services throw NotFoundException. Listing meters for an unknown site, or readings for an unknown meter, returns 404 rather than an empty list, because "doesn't exist" and "has none" are different answers.
•	Duplicate readings: the service throws DuplicateReadingException, as described in section 4.
•	Domain exceptions are mapped to HTTP responses in one place, ApiExceptionHandler, using .NET 8's IExceptionHandler. Using exceptions for 404 and 409 keeps the services simple. A Result<T> type would be more explicit but adds noise at this size.
7. Testing
Run all tests with dotnet test. They need neither SQL Server nor Docker, and they run in a few seconds.
Consumption calculation (pure unit tests, ConsumptionCalculatorTests)
•	Multi-reading scenario across three days. Five readings give daily values of 10.5, 19.5 and 2.0 kWh and a total of 32.0 kWh. The test also asserts that the total equals the sum of the daily values.
•	A pair spanning midnight is attributed to the date of the later reading.
•	Zero and one reading in range give a total of 0.
•	Inclusive boundaries: readings exactly at fromUtc and toUtc are included, and readings one tick outside are excluded.
•	Unordered input is ordered by timestamp before calculating.
•	A meter reset is ignored, and decreaseDetected is set.
•	A day with only a flat pair appears with 0.
•	fromUtc later than toUtc is rejected.
Duplicate reading timestamps (DuplicateReadingTests, in-memory SQLite)
•	A second reading with the same timestamp for the same meter is rejected, and the original value is kept.
•	Repeated duplicate attempts are all rejected the same way, never with a raw database error.
•	The same timestamp on a different meter is allowed.
•	Different timestamps on the same meter are allowed.
•	The unique index rejects a duplicate even when the service is bypassed, proving the database enforces the rule.
•	The EF model declares the unique index on (MeterId, TimestampUtc), which is what the SQL Server migration is generated from.
•	A reading for an unknown meter returns not found.
I used in-memory SQLite rather than the EF Core InMemory provider. InMemory doesn't enforce unique indexes, so a duplicate test against it would pass without proving anything.
Timestamp handling (UtcTimestampTests)
•	Z and offset timestamps are normalised to the same UTC instant.
•	Timestamps without a zone, and invalid values, are rejected.
•	The JSON converter round-trips values and rejects timestamps without a zone.
Manual checks
Through Swagger and EnergyMeter.Api.http: create and list each resource, a duplicate gives 409, a timestamp without a zone gives 400, an unknown meter gives 404, and the summary on the demo data returns the expected 61.2 kWh (30.0 + 31.2).
•	Build succeeded and all 25 tests passed in Visual Studio Test Explore
8. Assumptions and Limitations
Assumptions
•	All timestamps are UTC. Input must state its zone explicitly, and input with an offset is converted to UTC.
•	Readings are cumulative and expected to increase. A decrease is treated as a meter reset.
•	kWh values are non-negative, with at most three decimal places.
•	Authentication isn't required, as the brief states.
Optional functionality implemented
•	Meter reset handling, with a decreaseDetected flag in the summary.
•	Optional time-range filtering when listing readings.
•	Minimal demo data seeding (Development only), plus an equivalent SQL seed script.
•	An idempotent SQL schema script for SQL Server.
•	Swagger UI, and a .http file with ready-made example requests.
Known limitations and next steps
•	No pagination on reading lists. Readings grow without bound. The next step would be keyset pagination on (TimestampUtc, Id), which the existing index already supports.
•	Large summary ranges load the in-range readings into memory. That's fine for months of 15-minute data for one meter (about 3,000 rows a month). For very large ranges I'd calculate the differences in SQL with LAG() and group by date.
•	Consumption across a reset isn't recovered. The energy between the last pre-reset reading and the reset isn't counted, because that would require the meter's rollover value.
•	Serial numbers aren't unique. In a real system they probably should be, at least per site.
•	No update or delete endpoints, as the brief suggests.
•	No HTTP-level integration tests (WebApplicationFactory) and no tests against a real SQL Server (for example with Testcontainers). These would be the next tests to add. Program is already public partial to allow this.
•	No bulk ingest. A batch endpoint that reports duplicates per item would suit real meter data feeds.
9. AI Use
Tools used and what for
•	Claude Pro (Anthropic): The only AI tool used. I used it to scaffold the solution structure, entities, services, controllers and tests, generate the SQL schema script, and assist with drafting the README and this document.
Suggestions I rejected or changed
•	I made the final architectural decisions based on the assignment requirements rather than automatically accepting generated suggestions.
•	I chose Controllers over Minimal APIs to keep the API organised around its resources and make use of ASP.NET Core's built-in validation features.
•	I used in-memory SQLite instead of EF Core's InMemory provider for database behaviour tests because SQLite enforces unique indexes.
Decisions I made rather than accepting a default
•	Controllers rather than Minimal APIs (see Section 3).
•	SQL Server in Docker on port 14333 to avoid clashing with my existing local SQL Server.
•	Ignoring negative differences when handling meter resets, while flagging decreases in the summary response (see Section 5).
How I verified AI-generated code
•	I ran dotnet build and dotnet test locally and checked that the solution compiled and the automated tests passed.
•	I manually worked through the calculator test expectations, including 10.5 + 19.5 + 2.0 = 32.0 kWh.
•	I tested the API through Swagger and the .http file, verifying duplicate readings return 409, timestamps without a timezone return 400, unknown meters return 404, and the demo summary returns 61.2 kWh.
•	I confirmed that the EF Core migration matches the current model using dotnet ef migrations has-pending-model-changes.
•	I also verified edge cases involving unordered readings, inclusive date boundaries, readings spanning midnight, single-reading scenarios and meter resets.

