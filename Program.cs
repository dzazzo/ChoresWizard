using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Zazzo.ChoresWizard2000.Configuration;
using Zazzo.ChoresWizard2000.Data;
using Zazzo.ChoresWizard2000.HealthChecks;
using Zazzo.ChoresWizard2000.Services;
using Zazzo.ChoresWizard2000.Services.Export;

var builder = WebApplication.CreateBuilder(args);

// QuestPDF Community licence acknowledgement (issue #9). Free for organisations
// under $1M USD annual revenue; this household app qualifies. This is a real licence
// term, not decoration — QuestPDF throws at first document generation without it.
// The pinned package is 2026.7.2: never 'dotnet add package QuestPDF' unpinned, as
// the typo version 2202.8.2 sorts as newest forever and is a four-year-old build.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Ensure logs reliably reach the App Service log stream (stdout).
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    options.UseUtcTimestamp = true;
});

// Wire up Application Insights only when a connection string is configured
// (via the APPLICATIONINSIGHTS_CONNECTION_STRING env var / app setting). This
// keeps local dev free of telemetry noise and keeps secrets out of the repo.
var appInsightsConnectionString =
    builder.Configuration["ApplicationInsights:ConnectionString"]
    ?? builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetry(options =>
        options.ConnectionString = appInsightsConnectionString);
}

// Add Microsoft Identity authentication
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization(options =>
{
    // Require authenticated users by default. NOTE: this makes EVERY endpoint
    // require auth, so the health endpoints below MUST opt out via AllowAnonymous.
    options.FallbackPolicy = options.DefaultPolicy;
});

// Add services to the container.
builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();

builder.Services.AddRazorPages();

// Configure database based on environment
if (builder.Environment.IsDevelopment())
{
    // Use SQLite for local development
    builder.Services.AddDbContext<ChoresDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=chores.db"));
}
else
{
    // Use Azure SQL for production with Managed Identity.
    //
    // The database is General Purpose Serverless (GP_S_Gen5, autoPauseDelay 60m):
    // it auto-pauses when idle and returns error 40613 "Database is not currently
    // available" for ~30-60s while it resumes. That is a ROUTINE, EXPECTED condition
    // here, not a rare fault. EnableRetryOnFailure applies the SqlServer execution
    // strategy so 40613 (and 4060, 10928, 40501, ...) are retried automatically
    // instead of surfacing as a 500. Retry budget is sized to ride out the resume.
    //
    // Connection string name: "AzureSqlConnection" is the single canonical key,
    // read from appsettings.json (Authentication=Active Directory Managed Identity)
    // or overridden by an App Service connection string of the SAME name. See the
    // startup log line below, which reports the resolved auth mode so the live
    // config is never ambiguous.
    var azureSqlConnectionString = builder.Configuration.GetConnectionString("AzureSqlConnection");

    builder.Services.AddDbContext<ChoresDbContext>(options =>
        options.UseSqlServer(
            azureSqlConnectionString,
            sql => sql.EnableRetryOnFailure(
                maxRetryCount: 6,
                maxRetryDelay: TimeSpan.FromSeconds(15),
                errorNumbersToAdd: null))
        .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

    // Apply migrations resiliently in the background (out of the startup critical
    // path) so a paused/waking database can never take the host down. See issue #2.
    builder.Services.AddHostedService<DatabaseMigrationService>();
}

// Health checks:
//   /healthz -> liveness  (process is up; MUST NOT touch the database)
//   /readyz  -> readiness (actually connects to the database)
// The split is a real cost decision, not just tidiness: the DB is serverless and
// auto-pauses when idle. /healthz is the safe Always-On keep-alive target because
// it never touches the DB, so keeping the app warm does NOT keep the database awake
// (which would defeat auto-pause and bill at the vCore floor 24/7). DB checks live
// only on /readyz, for deployment gates and manual diagnosis.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>(
        "database",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready" });

// Time abstraction & household time zone. All "what month is it right now" decisions
// are made in this zone rather than UTC (issue #3), so a Pacific household on the
// evening of the 31st sees the correct month. TimeProvider is the built-in testable
// clock; production uses the system clock. .NET 10 resolves IANA ids cross-platform
// (developed on macOS, runs on Linux App Service).
builder.Services.Configure<HouseholdOptions>(
    builder.Configuration.GetSection(HouseholdOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);

var configuredTimeZoneId =
    builder.Configuration.GetSection(HouseholdOptions.SectionName)[nameof(HouseholdOptions.TimeZone)]
    ?? HouseholdOptions.DefaultTimeZone;

var timeZoneFellBack = false;
TimeZoneInfo householdTimeZone;
try
{
    householdTimeZone = TimeZoneInfo.FindSystemTimeZoneById(configuredTimeZoneId);
}
catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
{
    // Never let a bad/unknown id take the app down: fall back to the household default
    // and surface it clearly in the log after the host is built.
    householdTimeZone = TimeZoneInfo.FindSystemTimeZoneById(HouseholdOptions.DefaultTimeZone);
    timeZoneFellBack = true;
}

builder.Services.AddSingleton(householdTimeZone);

// Register services
builder.Services.AddScoped<SortingHatService>();

// Auto-resort (issue #5): an in-process scheduler that, on the last local day of the month,
// generates the NEXT month's assignments so the sheet is ready before the month starts. It is
// in-process (not a GitHub Actions cron) because Always On keeps the app running, which avoids a
// public trigger endpoint and a shared secret; the last-day decision is made in household-local
// time (America/Los_Angeles), so Pacific DST is handled correctly. Runs in every environment so
// it can be exercised locally; disable with AutoResort:Enabled=false.
builder.Services.Configure<AutoResortOptions>(
    builder.Configuration.GetSection(AutoResortOptions.SectionName));
builder.Services.AddScoped<AutoResortRunner>();
builder.Services.AddHostedService<AutoResortScheduler>();

// Chore export (issue #9): ICS feed, PDF, CSV. Token that gates the anonymous feed
// is bound from the "Export" section; it is empty in committed config and must be
// set out of band (user-secrets / Export__FeedToken app setting).
builder.Services.Configure<ExportOptions>(
    builder.Configuration.GetSection(ExportOptions.SectionName));
builder.Services.AddScoped<ChoreExportService>();

// Output cache for the anonymous ICS feed. This is a COST control, not a latency
// tweak: the Azure SQL database is a serverless tier that auto-pauses when idle, and
// Skylight polls the subscribed feed on its own schedule. Without caching, those polls
// alone would keep the database awake — and billing — around the clock. Assignments
// change at most once a month, so serving a cached copy costs nothing in freshness.
// The built-in policy only caches GET/HEAD 200 responses, so wrong-token 404s are
// never cached and cannot be used to flood the cache.
var feedCacheDuration = builder.Configuration
    .GetSection(ExportOptions.SectionName)
    .Get<ExportOptions>()?.ResolvedFeedCacheDuration
    ?? TimeSpan.FromSeconds(ExportOptions.DefaultFeedCacheSeconds);

builder.Services.AddOutputCache(options =>
{
    options.AddPolicy(ExportOptions.FeedCachePolicyName, policy => policy.Expire(feedCacheDuration));
});

var app = builder.Build();

// Report the resolved household time zone so month-boundary behavior is never
// ambiguous in the log stream (issue #3).
if (timeZoneFellBack)
{
    app.Logger.LogWarning(
        "Configured household time zone '{Configured}' could not be resolved; "
        + "falling back to '{Fallback}'.",
        configuredTimeZoneId, householdTimeZone.Id);
}
else
{
    app.Logger.LogInformation("Household time zone resolved: {TimeZone}.", householdTimeZone.Id);
}

// Make the live database configuration unambiguous in the App Service log stream,
// without ever logging secrets. Also flags the two footguns from issue #2:
// the dead "Active Directory Default" credential chain and SQL-auth passwords.
if (!app.Environment.IsDevelopment())
{
    var liveConnectionString = app.Configuration.GetConnectionString("AzureSqlConnection");
    if (string.IsNullOrWhiteSpace(liveConnectionString))
    {
        app.Logger.LogError(
            "No 'AzureSqlConnection' connection string is configured. Set it in appsettings.json "
            + "or as an App Service connection string named 'AzureSqlConnection'.");
    }
    else
    {
        var server = ExtractConnectionStringField(liveConnectionString, "Server")
            ?? ExtractConnectionStringField(liveConnectionString, "Data Source")
            ?? "(unknown)";
        var database = ExtractConnectionStringField(liveConnectionString, "Initial Catalog")
            ?? ExtractConnectionStringField(liveConnectionString, "Database")
            ?? "(unknown)";
        var authMode = ExtractConnectionStringField(liveConnectionString, "Authentication")
            ?? "(none - SQL auth)";
        var hasPassword = liveConnectionString.Contains("Password=", StringComparison.OrdinalIgnoreCase);

        app.Logger.LogInformation(
            "Azure SQL configuration resolved. Server={Server}; Database={Database}; Auth={AuthMode}.",
            server, database, authMode);

        if (authMode.Contains("Active Directory Default", StringComparison.OrdinalIgnoreCase))
        {
            app.Logger.LogWarning(
                "Connection string uses 'Active Directory Default', which walks the slow "
                + "DefaultAzureCredential chain on every cold start. Prefer "
                + "'Active Directory Managed Identity'.");
        }

        if (hasPassword)
        {
            app.Logger.LogWarning(
                "Connection string contains a SQL-auth password. Prefer the App Service "
                + "system-assigned managed identity ('Active Directory Managed Identity').");
        }
    }
}

// Initialize the local development database. Production migration runs in the
// DatabaseMigrationService background service registered above.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<ChoresDbContext>();
    context.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Output caching for the anonymous Skylight ICS feed (issue #9). Placed AFTER the
// authorization middleware on purpose: authorization always runs, and only the
// database query + ICS render are served from cache. Without this call the
// [OutputCache] attribute on ExportController.Feed is silently inert.
app.UseOutputCache();

app.MapStaticAssets();

// Liveness: does NOT touch the database. Anonymous so probes never hit the login
// redirect forced by the FallbackPolicy above.
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

// Readiness: runs checks tagged "ready" (the database check). Anonymous for the
// same reason.
app.MapHealthChecks("/readyz", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages();

app.Run();

// Extracts a single field's value from a SQL connection string by key
// (case-insensitive), or null if absent. Used only for non-secret diagnostics
// logging — never returns or logs Password/secret values.
static string? ExtractConnectionStringField(string connectionString, string key)
{
    foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var separatorIndex = part.IndexOf('=');
        if (separatorIndex <= 0)
        {
            continue;
        }

        var partKey = part[..separatorIndex].Trim();
        if (partKey.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            return part[(separatorIndex + 1)..].Trim();
        }
    }

    return null;
}

