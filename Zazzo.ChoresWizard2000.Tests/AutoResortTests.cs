using Microsoft.EntityFrameworkCore;
using Xunit;
using Zazzo.ChoresWizard2000.Data;
using Zazzo.ChoresWizard2000.Models;
using Zazzo.ChoresWizard2000.Services;

namespace Zazzo.ChoresWizard2000.Tests;

/// <summary>
/// Tests for auto-resort (issue #5): on the last day of the month in the household's local time
/// zone (America/Los_Angeles), generate the NEXT month's assignments — idempotently, without
/// overwriting a month a human may have hand-tuned. Every instant is expressed in UTC (as it
/// would arrive from the clock) and chosen so the local-time guard is what decides, which is why
/// the feature is correct across both 2025 Pacific DST transitions (spring-forward Mar 9,
/// fall-back Nov 2).
/// </summary>
public class AutoResortTests
{
    private static readonly TimeZoneInfo Pacific = TestData.Pacific;

    // A UTC instant that is 11:00 PM local on the last day of the given month, i.e. squarely on
    // the last local day but already the 1st in UTC — the case the naive UTC approach gets wrong.
    private static DateTimeOffset LateOnLastLocalDay(int year, int month)
    {
        var lastDay = new MonthPeriod(year, month).LastDay;
        var localLateEvening = lastDay.ToDateTime(new TimeOnly(23, 0), DateTimeKind.Unspecified);
        var offset = Pacific.GetUtcOffset(localLateEvening);
        return new DateTimeOffset(localLateEvening, offset).ToUniversalTime();
    }

    private static void SeedFamilyAndChores(ChoresDbContext ctx)
    {
        TestData.AddMember(ctx, "Mum", isParent: true);
        TestData.AddMember(ctx, "Dad", isParent: true);
        TestData.AddMember(ctx, "Ginny", isTeen: true);
        TestData.AddMember(ctx, "Ron");

        TestData.AddChore(ctx, "Dishes");
        TestData.AddChore(ctx, "Sweep");
        TestData.AddChore(ctx, "Trash");
        TestData.AddChore(ctx, "Feed cat");
        TestData.AddChore(ctx, "Laundry", frequency: ChoreFrequency.Weekly);
    }

    private static int CountAssignments(ChoresDbContext ctx, MonthPeriod period)
        => ctx.ChoreAssignments.Count(ca => ca.Month == period.Month && ca.Year == period.Year);

    [Fact]
    public async Task OnLastLocalDay_GeneratesNextMonth()
    {
        using var ctx = TestDbContext.Create();
        SeedFamilyAndChores(ctx);
        // 11 PM PST on Jan 31 2025 (== 07:00 UTC Feb 1). Naive UTC logic would think it's February.
        var clock = new FixedTimeProvider(LateOnLastLocalDay(2025, 1));
        var runner = TestData.CreateAutoResortRunner(ctx, clock);

        var result = await runner.RunAsync();

        Assert.Equal(AutoResortAction.Generated, result.Action);
        Assert.Equal(new MonthPeriod(2025, 2), result.TargetMonth);
        Assert.True(result.AssignmentCount > 0);
        Assert.True(CountAssignments(ctx, new MonthPeriod(2025, 2)) > 0);
    }

    [Fact]
    public async Task OnLastLocalDay_GeneratesNextMonth_NotTheCurrentMonth()
    {
        using var ctx = TestDbContext.Create();
        SeedFamilyAndChores(ctx);
        var clock = new FixedTimeProvider(LateOnLastLocalDay(2025, 1));
        var runner = TestData.CreateAutoResortRunner(ctx, clock);

        await runner.RunAsync();

        // February (the NEXT month) is generated; January (the current month) is left empty.
        Assert.True(CountAssignments(ctx, new MonthPeriod(2025, 2)) > 0);
        Assert.Equal(0, CountAssignments(ctx, new MonthPeriod(2025, 1)));
    }

    [Fact]
    public async Task LastDayOfDecember_RollsIntoNextJanuary()
    {
        using var ctx = TestDbContext.Create();
        SeedFamilyAndChores(ctx);
        // 11 PM PST Dec 31 2025 (== 07:00 UTC Jan 1 2026): the year must roll over too.
        var clock = new FixedTimeProvider(LateOnLastLocalDay(2025, 12));
        var runner = TestData.CreateAutoResortRunner(ctx, clock);

        var result = await runner.RunAsync();

        Assert.Equal(AutoResortAction.Generated, result.Action);
        Assert.Equal(new MonthPeriod(2026, 1), result.TargetMonth);
        Assert.True(CountAssignments(ctx, new MonthPeriod(2026, 1)) > 0);
    }

    [Theory]
    [InlineData(2025, 1, 15)]   // mid-January
    [InlineData(2025, 2, 1)]    // the 1st — the day AFTER a last day; must not fire
    [InlineData(2025, 6, 29)]   // June has 30 days, so the 29th is not the last day
    public async Task OnAnyOtherLocalDay_DoesNothing(int year, int month, int day)
    {
        using var ctx = TestDbContext.Create();
        SeedFamilyAndChores(ctx);
        // Noon local on the given day.
        var local = new DateOnly(year, month, day).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified);
        var clock = new FixedTimeProvider(
            new DateTimeOffset(local, Pacific.GetUtcOffset(local)).ToUniversalTime());
        var runner = TestData.CreateAutoResortRunner(ctx, clock);

        var result = await runner.RunAsync();

        Assert.Equal(AutoResortAction.SkippedNotLastDay, result.Action);
        Assert.Equal(0, ctx.ChoreAssignments.Count());
    }

    [Fact]
    public async Task JustAfterMidnightOnTheFirst_DoesNotFire()
    {
        using var ctx = TestDbContext.Create();
        SeedFamilyAndChores(ctx);
        // 12:30 AM PST on Feb 1 2025 (== 08:30 UTC Feb 1). It is the 1st locally, not a last day,
        // so nothing is generated — the guard reacts to the local date, not the UTC one.
        var clock = new FixedTimeProvider(new DateTimeOffset(2025, 2, 1, 8, 30, 0, TimeSpan.Zero));
        var runner = TestData.CreateAutoResortRunner(ctx, clock);

        var result = await runner.RunAsync();

        Assert.Equal(AutoResortAction.SkippedNotLastDay, result.Action);
        Assert.Equal(0, ctx.ChoreAssignments.Count());
    }

    [Fact]
    public async Task LastDayOfMarch_DuringPacificDaylightTime_Fires()
    {
        // March 2025 is the spring-forward month (DST begins Mar 9). Mar 31 is PDT (UTC-7):
        // 11 PM PDT == 06:00 UTC Apr 1. The local-date guard still says "last day of March".
        using var ctx = TestDbContext.Create();
        SeedFamilyAndChores(ctx);
        var clock = new FixedTimeProvider(LateOnLastLocalDay(2025, 3));
        var runner = TestData.CreateAutoResortRunner(ctx, clock);

        var result = await runner.RunAsync();

        Assert.Equal(AutoResortAction.Generated, result.Action);
        Assert.Equal(new MonthPeriod(2025, 4), result.TargetMonth);
    }

    [Fact]
    public async Task LastDayOfNovember_AfterFallBackToStandardTime_Fires()
    {
        // November 2025 is the fall-back month (DST ends Nov 2). Nov 30 is PST (UTC-8):
        // 11 PM PST == 07:00 UTC Dec 1. Still the last local day of November.
        using var ctx = TestDbContext.Create();
        SeedFamilyAndChores(ctx);
        var clock = new FixedTimeProvider(LateOnLastLocalDay(2025, 11));
        var runner = TestData.CreateAutoResortRunner(ctx, clock);

        var result = await runner.RunAsync();

        Assert.Equal(AutoResortAction.Generated, result.Action);
        Assert.Equal(new MonthPeriod(2025, 12), result.TargetMonth);
    }

    [Theory]
    [InlineData(2025, 3, 9)]    // spring-forward transition day (not a last day)
    [InlineData(2025, 11, 2)]   // fall-back transition day (not a last day)
    public async Task OnADstTransitionDay_ThatIsNotALastDay_DoesNothing(int year, int month, int day)
    {
        using var ctx = TestDbContext.Create();
        SeedFamilyAndChores(ctx);
        // Noon local avoids the ambiguous/skipped 1-2 AM window entirely.
        var local = new DateOnly(year, month, day).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified);
        var clock = new FixedTimeProvider(
            new DateTimeOffset(local, Pacific.GetUtcOffset(local)).ToUniversalTime());
        var runner = TestData.CreateAutoResortRunner(ctx, clock);

        var result = await runner.RunAsync();

        Assert.Equal(AutoResortAction.SkippedNotLastDay, result.Action);
        Assert.Equal(0, ctx.ChoreAssignments.Count());
    }

    [Fact]
    public async Task SameFixedUtcHour_FiresInAPstMonthButNotAPdtMonth_DriftIsHarmless()
    {
        // The crux of the DST hazard: a FIXED UTC hour drifts by an hour across the Pacific
        // offset boundary. 07:00 UTC is 11 PM the previous day in PST (UTC-8) but midnight the
        // same day in PDT (UTC-7). Because the local-date guard decides, the drift is harmless.

        // 07:00 UTC Dec 1 2025 -> 11 PM PST Nov 30 -> last day of November -> fires.
        using (var pstCtx = TestDbContext.Create())
        {
            SeedFamilyAndChores(pstCtx);
            var pstClock = new FixedTimeProvider(new DateTimeOffset(2025, 12, 1, 7, 0, 0, TimeSpan.Zero));
            var pstResult = await TestData.CreateAutoResortRunner(pstCtx, pstClock).RunAsync();

            Assert.Equal(AutoResortAction.Generated, pstResult.Action);
            Assert.Equal(new MonthPeriod(2025, 12), pstResult.TargetMonth);
        }

        // 07:00 UTC Jul 1 2025 -> 12:00 AM PDT Jul 1 -> the 1st, not a last day -> does nothing.
        using (var pdtCtx = TestDbContext.Create())
        {
            SeedFamilyAndChores(pdtCtx);
            var pdtClock = new FixedTimeProvider(new DateTimeOffset(2025, 7, 1, 7, 0, 0, TimeSpan.Zero));
            var pdtResult = await TestData.CreateAutoResortRunner(pdtCtx, pdtClock).RunAsync();

            Assert.Equal(AutoResortAction.SkippedNotLastDay, pdtResult.Action);
            Assert.Equal(0, pdtCtx.ChoreAssignments.Count());
        }
    }

    [Fact]
    public async Task RunningTwiceOnTheLastDay_IsIdempotent()
    {
        using var ctx = TestDbContext.Create();
        SeedFamilyAndChores(ctx);
        var clock = new FixedTimeProvider(LateOnLastLocalDay(2025, 1));
        var runner = TestData.CreateAutoResortRunner(ctx, clock);

        var first = await runner.RunAsync();
        var countAfterFirst = CountAssignments(ctx, new MonthPeriod(2025, 2));
        var second = await runner.RunAsync();
        var countAfterSecond = CountAssignments(ctx, new MonthPeriod(2025, 2));

        Assert.Equal(AutoResortAction.Generated, first.Action);
        Assert.Equal(AutoResortAction.SkippedAlreadyPopulated, second.Action);
        Assert.True(countAfterFirst > 0);
        Assert.Equal(countAfterFirst, countAfterSecond); // no duplicates on the second run
    }

    [Fact]
    public async Task WhenTargetMonthAlreadyHasAssignments_LeavesThemUntouched()
    {
        using var ctx = TestDbContext.Create();
        SeedFamilyAndChores(ctx);
        var member = ctx.FamilyMembers.First();
        var chore = ctx.Chores.First();
        // A human has already hand-tuned February with a single assignment.
        TestData.AddPreviousAssignment(ctx, member.Id, chore.Id, 2025, 2);

        var clock = new FixedTimeProvider(LateOnLastLocalDay(2025, 1));
        var runner = TestData.CreateAutoResortRunner(ctx, clock);

        var result = await runner.RunAsync();

        Assert.Equal(AutoResortAction.SkippedAlreadyPopulated, result.Action);
        Assert.Equal(0, result.AssignmentCount);
        // Exactly the one hand-tuned assignment remains; nothing was added or wiped.
        Assert.Equal(1, CountAssignments(ctx, new MonthPeriod(2025, 2)));
    }

    [Fact]
    public async Task EnsureMonthSortedAsync_OnEmptyMonth_Generates()
    {
        using var ctx = TestDbContext.Create();
        SeedFamilyAndChores(ctx);
        var service = TestData.CreateService(ctx);

        var result = await service.EnsureMonthSortedAsync(new MonthPeriod(2025, 5));

        Assert.Equal(SortOutcome.Generated, result.Outcome);
        Assert.True(result.AssignmentCount > 0);
    }
}
