using Microsoft.EntityFrameworkCore;

namespace Zazzo.ChoresWizard2000.Data;

/// <summary>
/// Runs the production database migration OUTSIDE the startup critical path.
///
/// Root cause #1 from issue #2: previously <c>context.Database.Migrate()</c> ran
/// synchronously before <c>app.Run()</c>. Any transient Azure SQL fault during a
/// cold start threw, the host never reached <c>app.Run()</c>, App Service restarted
/// the process, and the site returned 500s in a loop until an attempt happened to
/// succeed.
///
/// As a <see cref="BackgroundService"/>, this runs AFTER Kestrel starts listening,
/// so the host is already up and serving (health probes, error pages) while the
/// migration is attempted. It retries with exponential backoff and — critically —
/// never lets an exception escape, so it can never bring the host down. Readiness
/// is surfaced separately via the <c>/readyz</c> health check.
/// </summary>
public sealed class DatabaseMigrationService : BackgroundService
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseMigrationService> _logger;

    public DatabaseMigrationService(
        IServiceScopeFactory scopeFactory,
        ILogger<DatabaseMigrationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = InitialDelay;

        for (var attempt = 1; attempt <= MaxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ChoresDbContext>();

                _logger.LogInformation(
                    "Database migration starting (attempt {Attempt}/{MaxAttempts}).",
                    attempt, MaxAttempts);

                await context.Database.MigrateAsync(stoppingToken);

                _logger.LogInformation(
                    "Database migration completed successfully on attempt {Attempt}.",
                    attempt);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is shutting down — stop quietly.
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Database migration failed on attempt {Attempt}/{MaxAttempts}. Retrying in {Delay}s.",
                    attempt, MaxAttempts, delay.TotalSeconds);

                if (attempt == MaxAttempts)
                {
                    // Give up retrying, but do NOT throw — throwing here would stop the
                    // host (BackgroundServiceExceptionBehavior.StopHost). The site stays
                    // up; /readyz keeps reporting unhealthy until the DB recovers and the
                    // schema is applied (e.g. by the next deployment/restart).
                    _logger.LogCritical(
                        "Database migration failed after {MaxAttempts} attempts. The application will keep "
                        + "serving requests but the database schema may be out of date. See /readyz.",
                        MaxAttempts);
                    return;
                }

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                // Exponential backoff, capped.
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaxDelay.TotalMilliseconds));
            }
        }
    }
}
