using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Zazzo.ChoresWizard2000.Data;

namespace Zazzo.ChoresWizard2000.HealthChecks;

/// <summary>
/// Readiness check backing the anonymous <c>/readyz</c> endpoint.
///
/// Actually opens a connection to the database so a load balancer / uptime probe
/// can tell the difference between "process is alive" (<c>/healthz</c>) and
/// "process can serve real traffic that touches the DB" (<c>/readyz</c>). During an
/// Azure SQL cold start this reports Unhealthy until the database wakes up, instead
/// of the homepage throwing an unhandled 500.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly ChoresDbContext _context;

    public DatabaseHealthCheck(ChoresDbContext context)
    {
        _context = context;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _context.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database connection succeeded.")
                : HealthCheckResult.Unhealthy("Database is not reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database connection failed.", ex);
        }
    }
}
