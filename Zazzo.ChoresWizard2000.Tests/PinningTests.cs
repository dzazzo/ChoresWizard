using Xunit;
using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Tests;

public class PinningTests
{
    private const int Year = 2025;
    private const int Month = 6;

    [Fact]
    public async Task PinnedChore_AlwaysGoesToPinnedMember()
    {
        using var ctx = TestDbContext.Create();
        var parentA = TestData.AddMember(ctx, "ParentA", isParent: true);
        var kid = TestData.AddMember(ctx, "Kid");
        // Pinned to the kid even though other members exist and could be picked.
        TestData.AddChore(ctx, "Water plants", pinnedToFamilyMemberId: kid.Id);

        var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

        var single = Assert.Single(assignments);
        Assert.Equal(kid.Id, single.FamilyMemberId);
    }

    [Fact]
    public async Task PinnedChore_IsProcessedBeforeUnpinned()
    {
        using var ctx = TestDbContext.Create();
        var memberA = TestData.AddMember(ctx, "A", isParent: true);
        var memberB = TestData.AddMember(ctx, "B", isParent: true);

        // Two chores in the same category. Pinned-first ordering means A takes the
        // pinned one, so the category constraint forces the unpinned one onto B.
        TestData.AddChore(ctx, "Pinned kitchen", category: "Kitchen", pinnedToFamilyMemberId: memberA.Id);
        TestData.AddChore(ctx, "Unpinned kitchen", category: "Kitchen");

        var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

        Assert.Equal(2, assignments.Count);
        var pinned = assignments.Single(a => ctx.Chores.Single(c => c.Id == a.ChoreId).Name == "Pinned kitchen");
        var unpinned = assignments.Single(a => ctx.Chores.Single(c => c.Id == a.ChoreId).Name == "Unpinned kitchen");
        Assert.Equal(memberA.Id, pinned.FamilyMemberId);
        Assert.Equal(memberB.Id, unpinned.FamilyMemberId);
    }

    [Fact]
    public async Task PinToInactiveMember_DegradesGracefully()
    {
        using var ctx = TestDbContext.Create();
        var active = TestData.AddMember(ctx, "Active", isParent: true);
        var inactive = TestData.AddMember(ctx, "Inactive", isParent: true, isActive: false);
        // Pin references an inactive member (not present in the active pool).
        TestData.AddChore(ctx, "Orphan pinned", pinnedToFamilyMemberId: inactive.Id);

        var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

        // The chore is not dropped or crashing; it falls through to a normal eligible member.
        var single = Assert.Single(assignments);
        Assert.Equal(active.Id, single.FamilyMemberId);
        Assert.NotEqual(inactive.Id, single.FamilyMemberId);
    }
}