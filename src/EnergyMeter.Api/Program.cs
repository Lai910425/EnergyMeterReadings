using EnergyMeter.Api.Data;
using EnergyMeter.Api.Infrastructure;
using EnergyMeter.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- Services ----------

builder.Services
    .AddControllers(options =>
    {
        // Query-string timestamps follow the same "must carry a zone" rule as JSON bodies.
        options.ModelBinderProviders.Insert(0, new UtcDateTimeModelBinderProvider());
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
    });

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    var xml = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xml))
    {
        c.IncludeXmlComments(xml);
    }
});

// Connection string comes from configuration: appsettings.json, user secrets, or the
// environment variable ConnectionStrings__EnergyMeterDb (which overrides the file).
var connectionString = builder.Configuration.GetConnectionString("EnergyMeterDb")
    ?? throw new InvalidOperationException("Connection string 'EnergyMeterDb' is not configured.");

builder.Services.AddDbContext<EnergyMeterDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ISiteService, SiteService>();
builder.Services.AddScoped<IMeterService, MeterService>();
builder.Services.AddScoped<IReadingService, ReadingService>();

// ---------- Pipeline ----------

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

// ---------- Database bootstrap (opt-in, for a quick local start) ----------

if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EnergyMeterDbContext>();
    await db.Database.MigrateAsync();

    if (app.Configuration.GetValue<bool>("Database:SeedDemoData"))
    {
        await DemoDataSeeder.SeedAsync(db);
    }
}

app.Run();

// Exposed for test projects (e.g. WebApplicationFactory<Program>).
public partial class Program { }
