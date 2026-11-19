# Solution notes

## Overview

The API has one deployable project (`EnergyMeter.Api`) and one test project. Inside the API the code is split by
responsibility rather than into separate assemblies:

```
HTTP request
  → Controller          routing, model binding, 400s, status codes
  → Service             existence checks (404), duplicate rule (409), EF Core queries
  → ConsumptionCalculator   pure function: readings in → summary out
  → EnergyMeterDbContext    SQL Server via EF Core
```

At this size, separate Domain, Application and Infrastructure projects would add ceremony without adding clarity.
The folder boundaries are drawn so they could become projects later. `Domain/` has no dependency on EF Core
or ASP.NET Core.

## Key decisions

### Controllers rather than minimal APIs
- `[ApiController]` gives automatic `400 ValidationProblemDetails` from data annotations and
  `IValidatableObject`. With minimal APIs on .NET 8 I'd have to write that wiring myself.
- Three small controllers map directly to the three resources, so the API surface is easy to scan.
- Trade-off: slightly more ceremony and reflection-based startup. Minimal APIs would be faster,
  but performance isn't a concern at this scope.

### Resource design
- Meters are created and listed under their site (`/api/sites/{id}/meters`) because a meter can't exist
  without one. Once a meter exists it is addressed directly (`/api/meters/{id}`) to avoid deeply nested
  URLs, so readings and summaries hang off `/api/meters/{id}/…`.
- The summary is a read-only computed view (`GET …/summary`), not a stored resource.
- Listing an unknown site's meters or an unknown meter's readings returns `404`, not an empty list,
  because "doesn't exist" and "has none" are different answers.

### Timestamps
All timestamps are UTC (per the brief), and the code avoids guessing:
- **Input** must carry zone information (`Z` or an offset). `2026-11-01T10:00:00+02:00` is accepted and
  stored as `08:00:00Z`. A value without a zone is **rejected** (400) rather than assumed to be server-local or UTC,
  because silently misreading a timestamp corrupts consumption data.
- The same rule applies to JSON bodies (`UtcDateTimeJsonConverter`) and query strings
  (`UtcDateTimeModelBinder`). Both use `UtcTimestamp`, so the two paths can't drift apart.
- **Storage**: `datetime2`. Its value has no `Kind`, so a value converter marks values read back as
  `DateTimeKind.Utc`.
- **Output** is always `yyyy-MM-ddTHH:mm:ss[.fffffff]Z`.

### Data model
- `Site 1─* Meter 1─* Reading`, with cascade delete down the chain.
- `Reading.ValueKwh` is `decimal(18,3)`: exact arithmetic, no floating-point drift in sums, and Wh
  resolution. Values with more than 3 decimals are rejected (400) rather than rounded silently by SQL Server.
- `Reading.Id` is `bigint` because readings are by far the highest-volume table.
- A unique index on `(MeterId, TimestampUtc)` enforces the uniqueness rule. It also serves every
  reading query, which always filters by meter and time range. Because the index leads with `MeterId`,
  EF doesn't create a separate foreign-key index.

### Duplicate reading rule: rejected consistently with 409
Two layers, one outcome:
1. **Pre-check** in `ReadingService` handles the normal case with a clean `409 Conflict`.
2. The **unique index** is the source of truth. If two concurrent requests both pass the pre-check, the
   database rejects the second insert. The service catches `DbUpdateException`, checks that the row now
   exists, and throws the same `DuplicateReadingException`. The client gets the same 409 either way.

Checking the data instead of matching SQL Server error numbers (2601/2627) keeps the logic provider-agnostic,
so the same code path is exercised by the SQLite-based tests. The cost is one extra query on a rare failure path.

Idempotent "upsert" semantics (accept a resend of the identical value) were considered and rejected:
the brief asks for duplicates to be rejected, and a silent accept would hide client bugs.

### Consumption summary
`ConsumptionCalculator.Calculate(readings, fromUtc, toUtc)` is a pure static function. It applies the
brief's rules exactly:
- It filters to the inclusive range `[fromUtc, toUtc]`, then orders by timestamp. It does its own filtering and ordering,
  so the rules are correct no matter what the caller passes in.
- Total is latest minus earliest, or 0 with fewer than two readings.
- Daily: for each consecutive pair, the difference goes to the UTC date of the *later* reading, and only
  positive differences are summed. A day that has a pair attributed to it is listed even if its sum is 0.
  Days with no pair are omitted.

The service filters in SQL first, so only the relevant rows are loaded, and then calls the calculator.

### Meter resets (optional item, handled)
A decrease between consecutive readings is treated as a **meter reset or rollover** and contributes 0, which
is what "sum only positive differences" already implies. When a decrease is present, "latest minus earliest"
gives a misleading or even negative total, so in that case the total becomes the **sum of positive differences**.
This keeps `total == sum(daily)`. For normal increasing data the two formulas are mathematically identical,
so the brief's definition still holds. The response includes `decreaseDetected: true` so clients can flag the
period for review.

Not handled: the consumption that happened between the last pre-reset reading and the reset itself.
That needs to know the register's rollover value, which isn't part of this model.

### Error handling
Domain exceptions (`NotFoundException`, `DuplicateReadingException`) are mapped to RFC 7807 problem details
in one place (`ApiExceptionHandler`, using .NET 8's `IExceptionHandler`). Unexpected exceptions become a
generic 500 that doesn't leak internals. Using exceptions for 404 and 409 keeps the services simple. The
alternative is a `Result<T>` type, which is more explicit but adds noise at this size.

### Database setup
- **Docker** (`docker compose up -d`, SQL Server 2022 Developer) is the documented default. It's one command on
  any OS. It uses host port **14333** to avoid clashing with an existing local SQL Server on 1433.
- In Development the app runs `Migrate()` and seeds demo data on startup for a fast first run. Both are
  off by default outside Development. In production, migrations should be applied deliberately by a
  pipeline or a DBA, not by the app.
- `database/01_create_schema.sql` is the same schema as an idempotent script for DBAs or for running in
  SSMS. It inserts the `__EFMigrationsHistory` row, so EF and the script agree on the database state.
- `EnableRetryOnFailure` covers the container still starting up and transient network errors.

## Testing

`dotnet test` runs everything in a few seconds with no external dependencies.

| Area | How | What's covered |
|---|---|---|
| Summary calculation | Pure unit tests | Multi-reading, three-day breakdown; total equals sum of daily; pair over midnight goes to the later date; inclusive boundaries (exact `from`/`to` included, ±1 tick excluded); 0 and 1 readings; unordered input; reset handling; flat day shows 0; `from > to` |
| Duplicate rule | In-memory **SQLite** | Duplicate rejected and original row kept; repeated attempts all rejected the same way; same timestamp on another meter allowed; unique index rejects duplicates even when the service is bypassed; EF model declares the unique index |
| Timestamps | Pure unit tests | `Z` and offsets normalised to UTC; no zone rejected; JSON round-trip |

SQLite rather than the EF InMemory provider: InMemory doesn't enforce unique indexes, so a duplicate test
against it would prove nothing.

Not covered: HTTP-level integration tests (`WebApplicationFactory`) and tests against real SQL Server
(for example with Testcontainers). Both are the next things I'd add. `Program` is already `public partial` for that.

## Trade-offs and what I'd do next
- **No pagination** on reading lists. Readings grow without bound, so the next step is keyset pagination on
  `(TimestampUtc, Id)`, which the existing index already supports.
- **Serial numbers aren't unique.** In a real system they probably should be, at least per site.
- **Large summary ranges** load the readings in range into memory. That's fine for months of 15-minute data
  for one meter (about 3k rows a month). For very large ranges I'd push the pairwise difference into SQL with
  `LAG()` and group by date.
- **Bulk ingest:** real meters send batches. A `POST …/readings/batch` endpoint that reports duplicates per item
  would be the natural extension.
- Update and delete endpoints were left out, as the brief suggests.

## AI use

> ✏️ **Fill this in yourself before submitting.** The reviewers want *your* experience. The notes below are a
> starting point based on how this project was built. Edit them so they're accurate.

**Tools used**
- Claude (Anthropic) to scaffold the solution structure, entities, services, controllers and tests, and to
  draft the README and SOLUTION.md.
- *(Add any others: GitHub Copilot in Visual Studio, ChatGPT, etc.)*

**What I decided rather than accepting a default**
- I chose controllers over minimal APIs, Docker SQL Server, and "ignore negative deltas" for resets.
- *(Add at least one AI suggestion you **rejected or changed**, and why. For example: a suggestion to use
  the EF InMemory provider for the duplicate test, which wouldn't enforce the unique index; or accepting
  timestamps without a zone as UTC; or something you changed while reviewing the code.)*

**How I verified AI-generated code**
- I ran `dotnet build` and `dotnet test` locally. *(Note anything that failed and what you fixed.)*
- I worked through the calculator tests by hand (for example the three-day scenario: 10.5 + 19.5 + 2.0 = 32.0).
- I exercised the API through Swagger or the `.http` file: duplicate → 409, timestamp without a zone → 400,
  unknown meter → 404, summary on the demo data matches the expected values in `02_seed_demo_data.sql`.
- I checked the migration against the model with `dotnet ef migrations has-pending-model-changes`.
- *(Add any edge cases you tried yourself.)*
