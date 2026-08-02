using Microsoft.Extensions.Logging;
using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Services;

/// <summary>What a single auto-resort check decided to do.</summary>
public enum AutoResortAction
{
    /// <summary>Today is not the last local day of the month; nothing to do.</summary>
    SkippedNotLastDay,

    /// <summary>Today is the last local day and the next month was empty, so it was generated.</summary>
    Generated,

    /// <summary>Today is the last local day but the next month already had assignments; left untouched.</summary>
    SkippedAlreadyPopulated
}

/// <summary>Result of one <see cref="AutoResortRunner.RunAsync"/> invocation.</summary>
/// <param name="Action">What the check decided to do.</param>
/// <param name="LocalDate">The household-local date the decision was made for.</param>
/// <param name="TargetMonth">The month the check targeted (the month AFTER <paramref name="LocalDate"/>'s month).</param>
/// <param name="AssignmentCount">Assignments generated (0 unless <see cref="AutoResortAction.Generated"/>).</param>
public readonly record struct AutoResortRunResult(
    AutoResortAction Action,
    DateOnly LocalDate,
    MonthPeriod TargetMonth,
    int AssignmentCount);

/// <summary>
/// The decision + action for auto-resort (issue #5), kept as a plain injectable class so it
/// can be exercised deterministically in tests with a <see cref="TimeProvider"/> fake — the
/// hosting <see cref="AutoResortScheduler"/> is a thin timer around it.
///
/// The rule: on the <b>last day of the month in the household's local time zone</b>, ensure the
/// <b>next</b> month is sorted so the sheet is ready to load before the month starts. On any
/// other day it does nothing.
///
/// This is why the feature is DST-safe. "Last day of the month" is decided by
/// <see cref="MonthPeriod.IsLastDay"/> on a local <see cref="DateOnly"/> (pure calendar
/// arithmetic), after projecting the current instant into the household zone. A GitHub Actions
/// UTC cron would drift by an hour across a Pacific DST transition; here the UTC instant only
/// feeds the local-date projection, and the local-date guard is what actually decides — so the
/// drift is harmless.
/// </summary>
public sealed class AutoResortRunner
{
    private readonly SortingHatService _sortingHat;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger<AutoResortRunner> _logger;

    public AutoResortRunner(
        SortingHatService sortingHat,
        TimeProvider timeProvider,
        TimeZoneInfo timeZone,
        ILogger<AutoResortRunner> logger)
    {
        _sortingHat = sortingHat;
        _timeProvider = timeProvider;
        _timeZone = timeZone;
        _logger = logger;
    }

    /// <summary>
    /// Runs one auto-resort check. Returns what it decided so callers/tests can assert the
    /// behavior without depending on the wall clock.
    /// </summary>
    public async Task<AutoResortRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        // Project the current instant into the household zone ONCE, then reason purely on the
        // local calendar date. DST is entirely contained in this conversion.
        var localNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _timeZone);
        var localToday = DateOnly.FromDateTime(localNow.DateTime);
        var currentMonth = MonthPeriod.FromDate(localToday);

        if (!currentMonth.IsLastDay(localToday))
        {
            _logger.LogDebug(
                "Auto-resort: {LocalDate} is not the last day of {Month}; nothing to do.",
                localToday, currentMonth);
            return new AutoResortRunResult(
                AutoResortAction.SkippedNotLastDay, localToday, currentMonth.Next(), 0);
        }

        var targetMonth = currentMonth.Next();
        _logger.LogInformation(
            "Auto-resort: {LocalDate} is the last day of {Month}; ensuring {Target} is sorted.",
            localToday, currentMonth, targetMonth);

        var result = await _sortingHat.EnsureMonthSortedAsync(targetMonth, cancellationToken);

        var action = result.Outcome == SortOutcome.Generated
            ? AutoResortAction.Generated
            : AutoResortAction.SkippedAlreadyPopulated;

        return new AutoResortRunResult(action, localToday, targetMonth, result.AssignmentCount);
    }
}
