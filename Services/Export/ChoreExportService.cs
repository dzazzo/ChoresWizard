using Microsoft.EntityFrameworkCore;
using Zazzo.ChoresWizard2000.Data;
using Zazzo.ChoresWizard2000.Models;
using Zazzo.ChoresWizard2000.Models.Export;

namespace Zazzo.ChoresWizard2000.Services.Export;

/// <summary>
/// Loads a month's chore assignments from the database and turns them into the
/// export formats consumed by issue #9: an ICS feed (Skylight subscription), a
/// printable PDF, and a CSV fallback.
///
/// All month reasoning goes through <see cref="MonthPeriod"/> and the injected
/// <see cref="TimeProvider"/>/<see cref="TimeZoneInfo"/>; this service never reads
/// the wall clock directly, so the exported span is always the full 1st &#8594; last
/// day of the requested month regardless of when a sort actually ran.
/// </summary>
public sealed class ChoreExportService
{
    private readonly ChoresDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _timeZone;

    public ChoreExportService(ChoresDbContext context, TimeProvider timeProvider, TimeZoneInfo timeZone)
    {
        _context = context;
        _timeProvider = timeProvider;
        _timeZone = timeZone;
    }

    /// <summary>The current household month (local, not UTC).</summary>
    public MonthPeriod CurrentMonth() => MonthPeriod.Current(_timeProvider, _timeZone);

    /// <summary>
    /// Loads the assignments for <paramref name="period"/>, flattened and ordered by
    /// member then chore, ready for any of the format builders.
    /// </summary>
    public async Task<MonthlyChoreExport> BuildModelAsync(MonthPeriod period, CancellationToken cancellationToken = default)
    {
        var items = await _context.ChoreAssignments
            .Where(ca => ca.Month == period.Month && ca.Year == period.Year)
            .Include(ca => ca.FamilyMember)
            .Include(ca => ca.Chore)
            .Where(ca => ca.FamilyMember != null && ca.Chore != null)
            .OrderBy(ca => ca.FamilyMember!.Name)
            .ThenBy(ca => ca.Chore!.Name)
            .Select(ca => new ChoreExportItem(
                ca.FamilyMemberId,
                ca.ChoreId,
                ca.FamilyMember!.Name,
                ca.Chore!.Name,
                ca.Chore!.Frequency,
                ca.Chore!.Category))
            .ToListAsync(cancellationToken);

        return new MonthlyChoreExport(period, items);
    }

    /// <summary>Builds the ICS feed for <paramref name="period"/>.</summary>
    public async Task<string> BuildIcsAsync(MonthPeriod period, CancellationToken cancellationToken = default)
    {
        var model = await BuildModelAsync(period, cancellationToken);
        return SkylightIcsBuilder.Build(model, _timeProvider.GetUtcNow());
    }

    /// <summary>Builds the printable PDF for <paramref name="period"/>.</summary>
    public async Task<byte[]> BuildPdfAsync(MonthPeriod period, CancellationToken cancellationToken = default)
    {
        var model = await BuildModelAsync(period, cancellationToken);
        return ChorePdfBuilder.Build(model);
    }

    /// <summary>Builds the CSV export for <paramref name="period"/>.</summary>
    public async Task<string> BuildCsvAsync(MonthPeriod period, CancellationToken cancellationToken = default)
    {
        var model = await BuildModelAsync(period, cancellationToken);
        return ChoreCsvBuilder.Build(model);
    }
}
