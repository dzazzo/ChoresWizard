using Xunit;
using Zazzo.ChoresWizard2000.Models;
using Zazzo.ChoresWizard2000.Models.Export;
using Zazzo.ChoresWizard2000.Services.Export;

namespace Zazzo.ChoresWizard2000.Tests;

/// <summary>
/// Pins the ICS boundary semantics for the Skylight feed (issue #9). These are pure
/// (no clock, no DB): they assert the exact RFC 5545 shapes the owner specified —
/// all-day events with an EXCLUSIVE DTEND, an INCLUSIVE UNTIL, and the correct
/// daily vs weekly-on-Saturday RRULEs — plus that the exported span never depends on
/// when the sort ran.
/// </summary>
public class SkylightIcsBuilderTests
{
    // Jan 2026: the 1st is a Thursday, so the first Saturday is the 3rd; last day 31st.
    private static readonly MonthPeriod January2026 = new(2026, 1);

    // A run instant deliberately in a DIFFERENT month/day than the exported period,
    // to prove the exported dates come from MonthPeriod, not "today".
    private static readonly DateTimeOffset RanInJuly =
        new(2026, 7, 15, 9, 30, 0, TimeSpan.Zero);

    private static MonthlyChoreExport SingleItem(ChoreFrequency frequency)
    {
        var item = new ChoreExportItem(
            FamilyMemberId: 7,
            ChoreId: 42,
            MemberName: "Alex",
            ChoreName: "Feed dog",
            Frequency: frequency,
            Category: "Pets");
        return new MonthlyChoreExport(January2026, new[] { item });
    }

    private static string BuildSingle(ChoreFrequency frequency, DateTimeOffset? generatedAt = null)
        => SkylightIcsBuilder.Build(SingleItem(frequency), generatedAt ?? RanInJuly);

    [Fact]
    public void DailyChore_StartsOnTheFirst_WithExclusiveDtend()
    {
        var ics = BuildSingle(ChoreFrequency.Daily);

        // All-day: date-valued start on the 1st, end EXCLUSIVE on the 2nd (start + 1).
        Assert.Contains("DTSTART;VALUE=DATE:20260101", ics);
        Assert.Contains("DTEND;VALUE=DATE:20260102", ics);
    }

    [Fact]
    public void DailyChore_UsesFreqDaily_WithInclusiveUntilOnLastDay()
    {
        var ics = BuildSingle(ChoreFrequency.Daily);

        Assert.Contains("FREQ=DAILY", ics);
        // UNTIL is INCLUSIVE and is the month's true last day (Jan 31), as a DATE.
        Assert.Contains("UNTIL=20260131", ics);
    }

    [Fact]
    public void WeeklyChore_StartsOnFirstSaturday_WithExclusiveDtend()
    {
        var ics = BuildSingle(ChoreFrequency.Weekly);

        // First Saturday of Jan 2026 is the 3rd; exclusive end is the 4th.
        Assert.Contains("DTSTART;VALUE=DATE:20260103", ics);
        Assert.Contains("DTEND;VALUE=DATE:20260104", ics);
    }

    [Fact]
    public void WeeklyChore_UsesFreqWeeklyBySaturday_WithInclusiveUntil()
    {
        var ics = BuildSingle(ChoreFrequency.Weekly);

        Assert.Contains("FREQ=WEEKLY", ics);
        Assert.Contains("BYDAY=SA", ics);
        Assert.Contains("UNTIL=20260131", ics);
        // A weekly-on-Saturday chore must NOT serialize as daily.
        Assert.DoesNotContain("FREQ=DAILY", ics);
    }

    [Fact]
    public void ExportedSpan_IsIndependentOfWhenTheSortRan()
    {
        // Same period, two very different run instants (mid-Jan and mid-July).
        var ranMidJanuary = new DateTimeOffset(2026, 1, 20, 3, 0, 0, TimeSpan.Zero);

        var icsA = BuildSingle(ChoreFrequency.Daily, ranMidJanuary);
        var icsB = BuildSingle(ChoreFrequency.Daily, RanInJuly);

        // The DATE fields must be identical and anchored to the month, not the run date.
        foreach (var ics in new[] { icsA, icsB })
        {
            Assert.Contains("DTSTART;VALUE=DATE:20260101", ics);
            Assert.Contains("DTEND;VALUE=DATE:20260102", ics);
            Assert.Contains("UNTIL=20260131", ics);
        }

        // Only the DTSTAMP (generation instant) differs between the two runs.
        Assert.Contains("DTSTAMP:20260120T030000Z", icsA);
        Assert.Contains("DTSTAMP:20260715T093000Z", icsB);
    }

    [Fact]
    public void Uid_IsDeterministic_AcrossRuns_KeyedOnMemberChoreMonth()
    {
        var expected = "cw-7-42-202601@chores.zazzo.com";

        var icsA = BuildSingle(ChoreFrequency.Daily, new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero));
        var icsB = BuildSingle(ChoreFrequency.Daily, new DateTimeOffset(2026, 2, 9, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains($"UID:{expected}", icsA);
        Assert.Contains($"UID:{expected}", icsB);
    }

    [Fact]
    public void Summary_EncodesMemberAndCadenceInBrackets()
    {
        var ics = BuildSingle(ChoreFrequency.Daily);

        // Matches the issue's example shape: [ALEX] [DAILY] Feed dog
        Assert.Contains("SUMMARY:[ALEX] [DAILY] Feed dog", ics);
    }

    [Fact]
    public void Weekly_Summary_UsesWeeklyCadenceToken()
    {
        var ics = BuildSingle(ChoreFrequency.Weekly);

        Assert.Contains("SUMMARY:[ALEX] [WEEKLY] Feed dog", ics);
    }

    [Fact]
    public void DuplicateMemberChorePairs_ProduceASingleEvent()
    {
        var item = new ChoreExportItem(1, 2, "Sam", "Dishes", ChoreFrequency.Daily, null);
        var export = new MonthlyChoreExport(January2026, new[] { item, item });

        var ics = SkylightIcsBuilder.Build(export, RanInJuly);

        // The de-dupe guard means only one VEVENT/UID is emitted.
        var occurrences = CountOccurrences(ics, "UID:cw-1-2-202601@chores.zazzo.com");
        Assert.Equal(1, occurrences);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
