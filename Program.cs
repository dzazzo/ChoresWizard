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
    // EnableRetryOnFailure applies the SqlServer execution strategy so transient
    // Azure SQL faults during a cold start (40613, 4060, 10928, 40501, ...) are
    // retried automatically instead of surfacing to the user as a 500.
    builder.Services.AddDbContext<ChoresDbContext>(options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("AzureSqlConnection"),
            sql => sql.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null))
        .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

    // Apply migrations resiliently in the background (out of the startup critical
    // path) so a slow/waking database can never take the host down. See issue #2.
    builder.Services.AddHostedService<DatabaseMigrationService>();
}

// Health checks:
//   /healthz -> liveness  (process is up; no external dependencies)
//   /readyz  -> readiness (actually connects to the database)
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>(
        "database",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready" });

// Register services
builder.Services.AddScoped<SortingHatService>();

var app = builder.Build();

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
