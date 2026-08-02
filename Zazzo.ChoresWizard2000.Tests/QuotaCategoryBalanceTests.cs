using Xunit;
using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Tests;

public class QuotaCategoryBalanceTests
{
    private const int Year = 2025;
    private const int Month = 6;

    [Fact]
    public async Task CategoryConstraint_HonouredWhenSatisfiable()
    {
        using var ctx = TestDbContext.Create();
        TestData.AddMember(ctx, "A", isParent: true);
        TestData.AddMember(ctx, "B", isParent: true);
        // Two chores of the same category with two eligible members: each member
        // should end up with at most one chore from that category.
        TestData.AddChore(ctx, "Dishes", category: "Kitchen");
        TestData.AddChore(ctx, "Counters", category: "Kitchen");

        var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

        Assert.Equal(2, assignments.Count);
        var perMember = assignments.GroupBy(a => a.FamilyMemberId).Select(g => g.Count());
        Assert.All(perMember, count => Assert.Equal(1, count));
    }

    [Fact]
    public async Task CategoryConstraint_RelaxedRatherThanDroppingChore()
    {
        using var ctx = TestDbContext.Create();
        var parent = TestData.AddMember(ctx, "Parent", isParent: true);
        // Non-parent members exist but are ineligible for these AdultsOnly chores.
        TestData.AddMember(ctx, "Kid");

        // Two same-category chores, only one eligible member -> the constraint must be
        // relaxed so both are assigned (rather than one being silently dropped).
        TestData.AddChore(ctx, "Pay bills", category: "Finance", ageRestriction: AgeRestriction.AdultsOnly);
        TestData.AddChore(ctx, "Budget review", category: "Finance", ageRestriction: AgeRestriction.AdultsOnly);

        var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

        Assert.Equal(2, assignments.Count);
        Assert.All(assignments, a => Assert.Equal(parent.Id, a.FamilyMemberId));
    }

    [Fact]
    public async Task MaxQuota_IsSoftWhenOnlyOneEligibleMember()
    {
        using var ctx = TestDbContext.Create();
        var parent = TestData.AddMember(ctx, "Parent", isParent: true);

        // Five distinct-category daily chores, one eligible member. Daily max is 3, but
        // the algorithm still assigns every chore (max is a soft cap, not a hard drop).
        for (int i = 0; i < 5; i++)
        {
            TestData.AddChore(ctx, $"Daily {i}", category: $"Cat{i}");
        }

        var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

        Assert.Equal(5, assignments.Count);
        Assert.All(assignments, a => Assert.Equal(parent.Id, a.FamilyMemberId));
    }

    [Fact]
    public async Task BalanceAssignments_BringsUnderAssignedMembersUpToMinimum()
    {
        using var ctx = TestDbContext.Create();
        var a = TestData.AddMember(ctx, "A", isParent: true);
        var b = TestData.AddMember(ctx, "B", isParent: true);
        var c = TestData.AddMember(ctx, "C", isParent: true);

        // Six distinct-category daily chores across three eligible members. Daily min is
        // 2, so balancing must leave every member with at least the minimum.
        for (int i = 0; i < 6; i++)
        {
            TestData.AddChore(ctx, $"Daily {i}", category: $"Cat{i}");
        }

        var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

        Assert.Equal(6, assignments.Count);
        var counts = new[] { a.Id, b.Id, c.Id }
            .Select(id => assignments.Count(x => x.FamilyMemberId == id))
            .ToList();
        Assert.All(counts, count => Assert.True(count >= 2, $"expected >= 2, got {count}"));
    }

    [Fact]
    public async Task BalanceAssignments_DoesNotMoveChoreToAgeIneligibleMember()
    {
        using var ctx = TestDbContext.Create();
        var parent = TestData.AddMember(ctx, "Parent", isParent: true);
        var kid = TestData.AddMember(ctx, "Kid");

        // Three AdultsOnly daily chores. The kid is under the daily minimum (0) and the
        // parent is over it (3), but balancing must NOT hand AdultsOnly work to the kid.
        for (int i = 0; i < 3; i++)
        {
            TestData.AddChore(ctx, $"Adult daily {i}", category: $"Cat{i}",
                ageRestriction: AgeRestriction.AdultsOnly);
        }

        var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

        Assert.Equal(3, assignments.Count);
        Assert.All(assignments, x => Assert.Equal(parent.Id, x.FamilyMemberId));
        Assert.DoesNotContain(assignments, x => x.FamilyMemberId == kid.Id);
    }
}