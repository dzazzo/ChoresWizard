using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Models.Export;

/// <summary>
/// One assigned chore in a monthly export: who has it, what it is, and how often.
/// Flattened from <see cref="ChoreAssignment"/> so the export builders (ICS/PDF/CSV)
/// never touch EF entities directly and stay trivially unit-testable.
/// </summary>
/// <param name="FamilyMemberId">Stable member id, part of the deterministic ICS UID.</param>
/// <param name="ChoreId">Stable chore id, part of the deterministic ICS UID.</param>
/// <param name="MemberName">Display name of the assignee.</param>
/// <param name="ChoreName">Display name of the chore.</param>
/// <param name="Frequency">Cadence used to derive the recurrence rule.</param>
/// <param name="Category">Optional grouping category, surfaced in CSV.</param>
public sealed record ChoreExportItem(
    int FamilyMemberId,
    int ChoreId,
    string MemberName,
    string ChoreName,
    ChoreFrequency Frequency,
    string? Category);

/// <summary>
/// A month's worth of chore assignments to export. The <see cref="Period"/> is the
/// single source of truth for the exported span: every export covers the full
/// 1st &#8594; last day of <see cref="Period"/>, derived from <see cref="MonthPeriod"/>
/// and never from "today" (issue #9, depends on #3).
/// </summary>
public sealed record MonthlyChoreExport(
    MonthPeriod Period,
    IReadOnlyList<ChoreExportItem> Items);
