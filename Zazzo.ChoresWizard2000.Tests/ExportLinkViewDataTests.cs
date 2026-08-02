using Microsoft.AspNetCore.Mvc;
using Xunit;
using Zazzo.ChoresWizard2000.Controllers;
using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Tests;

/// <summary>
/// Guards the contract between the pages that list a month's assignments and the
/// export download buttons on them.
///
/// <para>The shared <c>_ExportActions</c> partial unboxes
/// <c>ViewData["ExportPeriod"]</c> into a <see cref="MonthPeriod"/> and builds the
/// PDF/CSV links from it. Two things can silently break the family's download:</para>
/// <list type="number">
///   <item>A controller stops publishing the key — the cast throws and the page 500s
///   right after a sort, which is the worst possible moment.</item>
///   <item>A controller publishes the <i>wrong</i> month — the button then downloads a
///   different month's chores (or an empty PDF) with no visible error at all. That is
///   why the month must come from the household clock, never UTC.</item>
/// </list>
/// </summary>
public class ExportLinkViewDataTests
{
    // 2026-09-01T02:00Z is still 2026-08-31 19:00 in Pacific. UTC says September;
    // the household is very much still in August. Any regression to DateTime.UtcNow
    // makes these fail rather than quietly linking to the wrong month.
    private static readonly DateTimeOffset LateAugustEveningPacific =
        new(2026, 9, 1, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SortingHatResults_PublishesTheHouseholdMonth_NotTheUtcMonth()
    {
        using var ctx = TestDbContext.Create();
        var clock = new FixedTimeProvider(LateAugustEveningPacific);
        var service = TestData.CreateService(ctx, timeProvider: clock, timeZone: TestData.Pacific);
        var controller = new SortingHatController(service);

        await controller.Results();

        var period = Assert.IsType<MonthPeriod>(controller.ViewData["ExportPeriod"]);
        Assert.Equal(2026, period.Year);
        Assert.Equal(8, period.Month);
    }

    [Fact]
    public async Task AssignmentsIndex_PublishesTheHouseholdMonth_NotTheUtcMonth()
    {
        using var ctx = TestDbContext.Create();
        var clock = new FixedTimeProvider(LateAugustEveningPacific);
        var controller = new AssignmentsController(ctx, clock, TestData.Pacific);

        await controller.Index();

        var period = Assert.IsType<MonthPeriod>(controller.ViewData["ExportPeriod"]);
        Assert.Equal(2026, period.Year);
        Assert.Equal(8, period.Month);
    }

    [Fact]
    public async Task SortingHatResults_PublishesExportPeriodMatchingTheMonthItRenders()
    {
        using var ctx = TestDbContext.Create();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 15, 20, 0, 0, TimeSpan.Zero));
        var service = TestData.CreateService(ctx, timeProvider: clock, timeZone: TestData.Pacific);

        var member = TestData.AddMember(ctx, "Alex");
        var chore = TestData.AddChore(ctx, "Feed dog");
        TestData.AddPreviousAssignment(ctx, member.Id, chore.Id, 2026, 1);

        var controller = new SortingHatController(service);
        var result = Assert.IsType<ViewResult>(await controller.Results());

        // The buttons must export exactly the month whose assignments are on screen —
        // not "whatever month it is when the user clicks".
        var period = Assert.IsType<MonthPeriod>(controller.ViewData["ExportPeriod"]);
        var rendered = Assert.IsAssignableFrom<IEnumerable<ChoreAssignment>>(result.Model);

        Assert.NotEmpty(rendered);
        Assert.All(rendered, a =>
        {
            Assert.Equal(period.Year, a.Year);
            Assert.Equal(period.Month, a.Month);
        });
    }
}
