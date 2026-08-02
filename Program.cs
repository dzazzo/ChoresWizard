using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Zazzo.ChoresWizard2000.Data;
using Zazzo.ChoresWizard2000.HealthChecks;
using Zazzo.ChoresWizard2000.Services;

var builder = WebApplication.CreateBuilder(args);

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

// Register services
builder.Services.AddScoped<SortingHatService>();

var app = builder.Build();

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

