using Xunit;
using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Tests;

public class EdgeCaseTests
{
    private const int Year = 2025;
    private const int Month = 6;

    [Fact]
    public async Task ZeroActiveMembers_ProducesNoAssignmentsAndDoesNotCrash()
    {
        using var ctx = TestDbContext.Create();
        // A chore exists but there is nobody to assign it to.
        TestData.AddChore(ctx, "Lonely chore");
        TestData.AddChore(ctx, "Group chore", assignToGroup: AssignToGroup.Everyone);

        var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

        Assert.Empty(assignments);
    }

    [Fact]
    public async Task ZeroActiveChores_ProducesNoAssignmentsAndDoesNotCrash()
    {
        using var ctx = TestDbContext.Create();
        TestData.AddMember(ctx, "A", isParent: true);
        TestData.AddMember(ctx, "B");

        var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

        Assert.Empty(assignments);
    }

    [Fact]
    public async Task InactiveMembersAndChores_AreIgnored()
    {
        using var ctx = TestDbContext.Create();
        var active = TestData.AddMember(ctx, "Active", isParent: true);
        var inactiveMember = TestData.AddMember(ctx, "InactiveMember", isParent: true, isActive: false);
        var activeChore = TestData.AddChore(ctx, "Active chore");
        var inactiveChore = TestData.AddChore(ctx, "Inactive chore", isActive: false);

        var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

        // Only the active chore is distributed, and only to the active member.
        Assert.All(assignments, a => Assert.Equal(active.Id, a.FamilyMemberId));
        Assert.All(assignments, a => Assert.Equal(activeChore.Id, a.ChoreId));
        Assert.DoesNotContain(assignments, a => a.FamilyMemberId == inactiveMember.Id);
        Assert.DoesNotContain(assignments, a => a.ChoreId == inactiveChore.Id);
        Assert.NotEmpty(assignments);
    }

    [Fact]
    public async Task Distribution_PersistsAssignmentsToDatabase()
    {
        using var ctx = TestDbContext.Create();
        var member = TestData.AddMember(ctx, "A", isParent: true);
        TestData.AddChore(ctx, "Persisted chore");

        await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

        var stored = ctx.ChoreAssignments.Where(a => a.Month == Month && a.Year == Year).ToList();
        var single = Assert.Single(stored);
        Assert.Equal(member.Id, single.FamilyMemberId);
    }
}