using Microsoft.EntityFrameworkCore;
using Xunit;
using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Tests;

/// <summary>
/// The main distribution loop honours anti-repetition, but a second pass —
/// <c>BalanceAssignments</c> — runs afterwards to top up anyone left below the
/// per-frequency minimum. That pass used to reassign chores looking only at
/// quotas and age restrictions, so it could hand a member straight back the very
/// chore the main loop had just moved away from them.
///
/// This was not theoretical: a child was assigned the same weekly chore two
/// months running in production.
///
/// These tests sweep many seeds rather than pinning one, because whether the
/// balancer runs at all — and which assignment it happens to grab — depends on
/// the shuffled chore order. A single seed would prove almost nothing.
/// </summary>
public class BalancerAntiRepetitionTests
{
    private const int SeedSweep = 60;

    /// <summary>
    /// Weekly quotas are min 1 / max 2, so with three members and three chores a
    /// member can easily finish the main loop with none — at which point the
    /// balancer takes a chore off somebody who has two. If the member it is
    /// topping up held one of those chores last month, that chore must not be the
    /// one handed over while any other donor is available.
    /// </summary>
    [Fact]
    public async Task Balancer_NeverHandsBackLastMonthsChore_WhenAnotherDonorExists()
    {
        for (var seed = 1; seed <= SeedSweep; seed++)
        {
            using var ctx = TestDbContext.Create();

            var mom = TestData.AddMember(ctx, "Mom", isParent: true);
            var alex = TestData.AddMember(ctx, "Alex", isTeen: true);
            var graham = TestData.AddMember(ctx, "Graham");

            var dishwasher = TestData.AddChore(
                ctx, "Unload top two racks of dishwasher", ChoreFrequency.Weekly);
            TestData.AddChore(ctx, "Take out recycling", ChoreFrequency.Weekly);
            TestData.AddChore(ctx, "Sweep the porch", ChoreFrequency.Weekly);

            // Graham had the dishwasher in July.
            TestData.AddPreviousAssignment(ctx, graham.Id, dishwasher.Id, 2026, 7);

            var service = TestData.CreateService(ctx, seed);
            await service.DistributeChoresAsync(2026, 8);

            var grahamsChores = await ctx.ChoreAssignments
                .Where(a => a.Year == 2026 && a.Month == 8 && a.FamilyMemberId == graham.Id)
                .Select(a => a.ChoreId)
                .ToListAsync();

            Assert.DoesNotContain(dishwasher.Id, grahamsChores);
        }
    }

    /// <summary>
    /// The balancer used to pick the first over-quota assignment, check age
    /// eligibility, and silently do nothing when it failed — without removing that
    /// assignment from the pool. The next iteration picked the same ineligible
    /// assignment again, so the loop spun and the member stayed below minimum.
    ///
    /// Here every donor chore Alex holds is adults-only except one, so a correct
    /// balancer has to skip past the ineligible ones to find it.
    /// </summary>
    [Fact]
    public async Task Balancer_SkipsIneligibleDonors_InsteadOfStalling()
    {
        var balanced = 0;

        for (var seed = 1; seed <= SeedSweep; seed++)
        {
            using var ctx = TestDbContext.Create();

            var mom = TestData.AddMember(ctx, "Mom", isParent: true);
            var graham = TestData.AddMember(ctx, "Graham");

            // Mom can hold all of these; Graham is only ever eligible for the last.
            TestData.AddChore(ctx, "Pay the bills", ChoreFrequency.Weekly, AgeRestriction.AdultsOnly);
            TestData.AddChore(ctx, "Order groceries", ChoreFrequency.Weekly, AgeRestriction.AdultsOnly);
            var forGraham = TestData.AddChore(ctx, "Sweep the porch", ChoreFrequency.Weekly);

            var service = TestData.CreateService(ctx, seed);
            await service.DistributeChoresAsync(2026, 8);

            var grahamsCount = await ctx.ChoreAssignments
                .CountAsync(a => a.Year == 2026 && a.Month == 8 && a.FamilyMemberId == graham.Id);

            // Weekly minimum is 1; Graham is eligible for exactly one chore, so a
            // balancer that walks past the adults-only donors always reaches it.
            Assert.Equal(1, grahamsCount);
            balanced++;
        }

        Assert.Equal(SeedSweep, balanced);
    }

    /// <summary>
    /// Anti-repetition is a preference, not a hard guarantee: leaving a child with
    /// nothing to do is worse than repeating a chore. When every chore a member is
    /// eligible for was theirs last month, the balancer should still top them up.
    /// This pins the deliberate trade-off so it is not "fixed" by accident.
    /// </summary>
    [Fact]
    public async Task Balancer_StillFillsMinimum_WhenEveryCandidateWouldRepeat()
    {
        using var ctx = TestDbContext.Create();

        var alex = TestData.AddMember(ctx, "Alex", isTeen: true);
        var graham = TestData.AddMember(ctx, "Graham");

        var a = TestData.AddChore(ctx, "Unload top two racks of dishwasher", ChoreFrequency.Weekly);
        var b = TestData.AddChore(ctx, "Take out recycling", ChoreFrequency.Weekly);

        // Graham had both weekly chores last month, so no non-repeating option exists.
        TestData.AddPreviousAssignment(ctx, graham.Id, a.Id, 2026, 7);
        TestData.AddPreviousAssignment(ctx, graham.Id, b.Id, 2026, 7);

        var service = TestData.CreateService(ctx, seed: 12345);
        await service.DistributeChoresAsync(2026, 8);

        var grahamsCount = await ctx.ChoreAssignments
            .CountAsync(x => x.Year == 2026 && x.Month == 8 && x.FamilyMemberId == graham.Id);

        Assert.Equal(1, grahamsCount);
    }
}
