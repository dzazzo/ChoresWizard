using Microsoft.Extensions.Options;
using Zazzo.ChoresWizard2000.Configuration;

namespace Zazzo.ChoresWizard2000.Services;

/// <summary>
/// In-process scheduler for auto-resort (issue #5). A thin timer around
/// <see cref="AutoResortRunner"/>: it wakes on an interval, opens a scope, and runs one check.
/// All month/day/DST reasoning lives in the runner.
///
/// Why in-process rather than a GitHub Actions cron: with App Service "Always On" enabled the
/// app runs continuously, so a background timer is reliable — and this avoids a public trigger
/// endpoint and a shared secret entirely (an endpoint that rewrites a month of chores is real
/// attack surface, even for a family app). It also sidesteps the fact that Actions cron is
/// UTC-only with no "last day of month" expression: here the local-time guard in the runner
/// decides, so Pacific DST is handled correctly.
///
/// Follows the established <see cref="Data.DatabaseMigrationService"/> pattern: it NEVER lets an
/// exception escape, so a transient database fault (e.g. the serverless DB resuming) can only
/// fail one check, not bring the host down. The next interval retries.
/// </summary>
public sealed class AutoResortScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoResortScheduler> _logger;
    private readonly AutoResortOptions _options;

    public AutoResortScheduler(
        IServiceScopeFactory scopeFactory,
        ILogger<AutoResortScheduler> logger,
        IOptions<AutoResortOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Auto-resort scheduler is disabled (AutoResort:Enabled=false); not scheduling.");
            return;
        }

        _logger.LogInformation(
            "Auto-resort scheduler started; checking every {IntervalHours}h for the last local day of the month.",
            _options.CheckInterval.TotalHours);

        // Let the background database migration get a head start before the first check.
        try
        {
            await Task.Delay(_options.StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Check once immediately (the app may have started ON the last day), then on the interval.
        await RunOnceSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(_options.CheckInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down — stop quietly.
        }
    }

    private async Task RunOnceSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<AutoResortRunner>();
            await runner.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down — ignore.
        }
        catch (Exception ex)
        {
            // Never throw: a failed check must not stop the host. Retried next interval.
            _logger.LogError(
                ex, "Auto-resort check failed; the host stays up and will retry on the next interval.");
        }
    }
}
