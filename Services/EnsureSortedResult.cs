namespace Zazzo.ChoresWizard2000.Services;

/// <summary>What <see cref="SortingHatService.EnsureMonthSortedAsync"/> did.</summary>
public enum SortOutcome
{
    /// <summary>The month was empty, so a fresh set of assignments was generated.</summary>
    Generated,

    /// <summary>The month already had assignments and was left untouched (no overwrite).</summary>
    AlreadyPopulated
}

/// <summary>Outcome of an idempotent "ensure this month is sorted" call.</summary>
/// <param name="Outcome">Whether assignments were generated or the month was already populated.</param>
/// <param name="AssignmentCount">Number of assignments generated (0 when already populated).</param>
public readonly record struct EnsureSortedResult(SortOutcome Outcome, int AssignmentCount);
