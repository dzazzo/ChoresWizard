using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;
using Zazzo.ChoresWizard2000.Configuration;
using Zazzo.ChoresWizard2000.Models;
using Zazzo.ChoresWizard2000.Services.Export;

namespace Zazzo.ChoresWizard2000.Controllers;

/// <summary>
/// Serves the month's chore export in the three formats from issue #9:
/// <list type="bullet">
///   <item><b>ICS feed</b> — anonymous, token-gated, for Skylight to subscribe to.</item>
///   <item><b>PDF</b> — authenticated, printable fridge chart.</item>
///   <item><b>CSV</b> — authenticated, generic spreadsheet fallback.</item>
/// </list>
/// </summary>
public sealed class ExportController : Controller
{
    private readonly ChoreExportService _exportService;
    private readonly ExportOptions _options;

    public ExportController(ChoreExportService exportService, IOptions<ExportOptions> options)
    {
        _exportService = exportService;
        _options = options.Value;
    }

    /// <summary>
    /// The anonymous iCalendar feed Skylight subscribes to:
    /// <c>GET /feed/{token}/chores.ics</c>.
    ///
    /// <para><b>Anonymous by necessity.</b> Skylight cannot authenticate, and
    /// <c>Program.cs</c> sets a global <c>FallbackPolicy</c> that requires auth on
    /// every endpoint, so this action opts out explicitly with
    /// <see cref="AllowAnonymousAttribute"/>. Without it the feed would return a
    /// login redirect and Skylight could never subscribe.</para>
    ///
    /// <para><b>The token in the route is the only protection.</b> It is read from
    /// configuration (never hardcoded), compared in constant time, and never logged.
    /// While no token is configured the feed is disabled and returns 404, so an
    /// unconfigured deployment never exposes chores anonymously.</para>
    /// <para><b>Output-cached deliberately.</b> The database is a serverless tier that
    /// auto-pauses when idle. Skylight polls this feed on its own schedule, so querying
    /// on every poll would keep the database awake continuously and bill for it. The
    /// response is cached (see <see cref="ExportOptions.FeedCacheSeconds"/>); assignments
    /// change at most once a month, so this costs nothing in freshness. Only 200s are
    /// cached, so a flood of wrong-token 404s cannot fill the cache.</para>
    /// </summary>
    [AllowAnonymous]
    [OutputCache(PolicyName = ExportOptions.FeedCachePolicyName)]
    [HttpGet("feed/{token}/chores.ics")]
    public async Task<IActionResult> Feed(string token, CancellationToken cancellationToken)
    {
        var configured = _options.FeedToken;

        // Feed is disabled until an unguessable token is configured out of band.
        // Returning 404 (not 401/403) keeps the endpoint's existence unremarkable.
        if (string.IsNullOrEmpty(configured) || !IsTokenValid(token, configured))
        {
            return NotFound();
        }

        var period = _exportService.CurrentMonth();
        var ics = await _exportService.BuildIcsAsync(period, cancellationToken);
        // Inline (no filename) — Skylight reads the body directly.
        return File(Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8");
    }

    /// <summary>
    /// Authenticated printable PDF. Defaults to the current household month; an
    /// explicit <c>?year=&amp;month=</c> exports a different month.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Pdf(int? year, int? month, CancellationToken cancellationToken)
    {
        if (!TryResolvePeriod(year, month, out var period))
        {
            return BadRequest("Invalid year or month.");
        }

        var pdf = await _exportService.BuildPdfAsync(period, cancellationToken);
        return File(pdf, "application/pdf", $"chores-{period.Year:D4}-{period.Month:D2}.pdf");
    }

    /// <summary>
    /// Authenticated CSV fallback. Defaults to the current household month; an
    /// explicit <c>?year=&amp;month=</c> exports a different month.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Csv(int? year, int? month, CancellationToken cancellationToken)
    {
        if (!TryResolvePeriod(year, month, out var period))
        {
            return BadRequest("Invalid year or month.");
        }

        var csv = await _exportService.BuildCsvAsync(period, cancellationToken);
        return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8",
            $"chores-{period.Year:D4}-{period.Month:D2}.csv");
    }

    // Resolves the requested month. Missing year/month means "current household
    // month"; a partially/ fully specified value is validated against MonthPeriod's
    // ranges. Uses MonthPeriod exclusively — never DateTime.Now.
    private bool TryResolvePeriod(int? year, int? month, out MonthPeriod period)
    {
        if (year is null && month is null)
        {
            period = _exportService.CurrentMonth();
            return true;
        }

        var current = _exportService.CurrentMonth();
        var resolvedYear = year ?? current.Year;
        var resolvedMonth = month ?? current.Month;

        if (resolvedMonth is < 1 or > 12 || resolvedYear is < 1 or > 9999)
        {
            period = default;
            return false;
        }

        period = new MonthPeriod(resolvedYear, resolvedMonth);
        return true;
    }

    // Constant-time comparison so a real token is never distinguishable from a wrong
    // one by response timing. Differing lengths simply compare unequal.
    private static bool IsTokenValid(string provided, string configured)
    {
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var configuredBytes = Encoding.UTF8.GetBytes(configured);
        return CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);
    }
}
