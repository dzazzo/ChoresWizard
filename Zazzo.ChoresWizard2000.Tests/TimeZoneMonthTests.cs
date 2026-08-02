using Xunit;
using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Tests;

/// <summary>
/// Regression tests for issue #3: month determination must be made in the household's
/// local time zone, not UTC. Each instant below is expressed in UTC (as it would be
/// read from <c>DateTime.UtcNow</c>) and is chosen so the old UTC-based logic gives the
/// wrong month; the new local-projection logic must give the right one. DST is
/// exercised on both sides of both 2025 US transitions (spring forward Mar 9,
/// fall back Nov 2).
/// </summary>
public class TimeZoneMonthTests
{
    private static readonly TimeZoneInfo Pacific = TestData.Pacific;

    [Fact]
    public void LastEveningOfJanuary_PST_ResolvesToJanuary_NotFebruary()
    {
        // 5:00 PM PST on Jan 31 2025 == 01:00 UTC on Feb 1. UtcNow.Month would say February.
        var instant = new DateTimeOffset(2025, 2, 1, 1, 0, 0, TimeSpan.Zero);

        var month = MonthPeriod.FromInstant(instant, Pacific);

        Assert.Equal(new MonthPeriod(2025, 1), month);
    }

    [Fact]
    public void LastEveningOfDecember_PST_ResolvesToDecember_NotNextJanuary()
    {
        // 6:00 PM PST on Dec 31 2025 == 02:00 UTC on Jan 1 2026. The old logic rolls
        // over BOTH the month and the year — the worst case of the bug.
        var instant = new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero);

        var month = MonthPeriod.FromInstant(instant, Pacific);

        Assert.Equal(new MonthPeriod(2025, 12), month);
    }

    [Fact]
    public void LastEveningOfMarch_PDT_ResolvesToMarch_NotApril()
    {
        // After spring-forward (DST active, UTC-7). 11:00 PM PDT Mar 31 2025 == 06:00 UTC Apr 1.
        var instant = new DateTimeOffset(2025, 4, 1, 6, 0, 0, TimeSpan.Zero);

        var month = MonthPeriod.FromInstant(instant, Pacific);

        Assert.Equal(new MonthPeriod(2025, 3), month);
    }

    [Fact]
    public void LastEveningOfOctober_PDT_ResolvesToOctober_NotNovember()
    {
        // Still DST (fall-back is Nov 2). 11:00 PM PDT Oct 31 2025 == 06:00 UTC Nov 1.
        var instant = new DateTimeOffset(2025, 11, 1, 6, 0, 0, TimeSpan.Zero);

        var month = MonthPeriod.FromInstant(instant, Pacific);

        Assert.Equal(new MonthPeriod(2025, 10), month);
    }

    [Theory]
    // Spring forward: Mar 9 2025, local 02:00 PST -> 03:00 PDT.
    [InlineData(2025, 3, 9, 9, 30, 3)]   // 01:30 PST (UTC-8), before the gap -> March
    [InlineData(2025, 3, 9, 10, 30, 3)]  // 03:30 PDT (UTC-7), after the gap  -> March
    // Fall back: Nov 2 2025, local 02:00 PDT -> 01:00 PST.
    [InlineData(2025, 11, 2, 8, 30, 11)]  // 01:30 PDT (UTC-7), first pass  -> November
    [InlineData(2025, 11, 2, 9, 30, 11)]  // 01:30 PST (UTC-8), second pass -> November
    public void MonthDetermination_IsStableAcrossDstTransitions(
        int utcYear, int utcMonth, int utcDay, int utcHour, int utcMinute, int expectedMonth)
    {
        var instant = new DateTimeOffset(utcYear, utcMonth, utcDay, utcHour, utcMinute, 0, TimeSpan.Zero);

        var month = MonthPeriod.FromInstant(instant, Pacific);

        Assert.Equal(expectedMonth, month.Month);
        Assert.Equal(2025, month.Year);
    }

    [Fact]
    public void Contains_UsesLocalProjection_ForUtcInstants()
    {
        var january = new MonthPeriod(2025, 1);
        // 01:00 UTC Feb 1 is still Jan 31 in Pacific.
        var lastEvening = new DateTimeOffset(2025, 2, 1, 1, 0, 0, TimeSpan.Zero);

        Assert.True(january.Contains(lastEvening, Pacific));
        Assert.False(january.Next().Contains(lastEvening, Pacific));
    }

    [Fact]
    public void Service_GetCurrentMonth_UsesInjectedClockAndZone()
    {
        using var ctx = TestDbContext.Create();
        // 5:00 PM PST on Jan 31 2025 (01:00 UTC Feb 1). Old UtcNow logic would say February.
        var clock = new FixedTimeProvider(new DateTimeOffset(2025, 2, 1, 1, 0, 0, TimeSpan.Zero));

        var service = TestData.CreateService(ctx, timeProvider: clock, timeZone: Pacific);

        Assert.Equal(new MonthPeriod(2025, 1), service.GetCurrentMonth());
    }

    [Fact]
    public async Task Service_GetCurrentMonthAssignments_ReturnsLocalMonth_NotUtcMonth()
    {
        using var ctx = TestDbContext.Create();
        var member = TestData.AddMember(ctx, "Harry", isParent: true);
        var chore = TestData.AddChore(ctx, "Dishes");
        // Two months of assignments exist; the clock decides which one is "current".
        TestData.AddPreviousAssignment(ctx, member.Id, chore.Id, 2025, 1);
        TestData.AddPreviousAssignment(ctx, member.Id, chore.Id, 2025, 2);

        // 5:00 PM PST on Jan 31 2025 == 01:00 UTC Feb 1: the app must still be in January.
        var clock = new FixedTimeProvider(new DateTimeOffset(2025, 2, 1, 1, 0, 0, TimeSpan.Zero));
        var service = TestData.CreateService(ctx, timeProvider: clock, timeZone: Pacific);

        var assignments = await service.GetCurrentMonthAssignmentsAsync();

        Assert.All(assignments, a => Assert.Equal(1, a.Month));
        Assert.All(assignments, a => Assert.Equal(2025, a.Year));
        Assert.NotEmpty(assignments);
    }
}
