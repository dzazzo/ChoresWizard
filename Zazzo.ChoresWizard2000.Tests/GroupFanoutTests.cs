using Xunit;
using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Tests;

public class GroupFanoutTests
{
    private const int Year = 2025;
    private const int Month = 6;

    private static (TestDbContext ctx, FamilyMember parent, FamilyMember teen, FamilyMember kid1, FamilyMember kid2) Family()
    {
        var ctx = TestDbContext.Create();
        var parent = TestData.AddMember(ctx, "Parent", isParent: true);
        var teen = TestData.AddMember(ctx, "Teen", isTeen: true);
        var kid1 = TestData.AddMember(ctx, "Kid1");
        var kid2 = TestData.AddMember(ctx, "Kid2");
        return (ctx, parent, teen, kid1, kid2);
    }

    [Fact]
    public async Task AllKids_ExcludesTeensAndParents()
    {
        var (ctx, parent, teen, kid1, kid2) = Family();
        using (ctx)
        {
            TestData.AddChore(ctx, "Clean your room", assignToGroup: AssignToGroup.AllKids);

            var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

            var memberIds = assignments.Select(a => a.FamilyMemberId).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { kid1.Id, kid2.Id }.OrderBy(x => x), memberIds);
            Assert.DoesNotContain(assignments, a => a.FamilyMemberId == parent.Id || a.FamilyMemberId == teen.Id);
        }
    }

    [Fact]
    public async Task AllTeens_HitsOnlyTeens()
    {
        var (ctx, parent, teen, kid1, kid2) = Family();
        using (ctx)
        {
            TestData.AddChore(ctx, "Mow the lawn", assignToGroup: AssignToGroup.AllTeens);

            var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

            var single = Assert.Single(assignments);
            Assert.Equal(teen.Id, single.FamilyMemberId);
        }
    }

    [Fact]
    public async Task AllAdults_HitsOnlyParents()
    {
        var (ctx, parent, teen, kid1, kid2) = Family();
        using (ctx)
        {
            TestData.AddChore(ctx, "Do the taxes", assignToGroup: AssignToGroup.AllAdults);

            var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

            var single = Assert.Single(assignments);
            Assert.Equal(parent.Id, single.FamilyMemberId);
        }
    }

    [Fact]
    public async Task AllChildren_HitsKidsAndTeensButNotParents()
    {
        var (ctx, parent, teen, kid1, kid2) = Family();
        using (ctx)
        {
            TestData.AddChore(ctx, "Tidy playroom", assignToGroup: AssignToGroup.AllChildren);

            var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

            var memberIds = assignments.Select(a => a.FamilyMemberId).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { teen.Id, kid1.Id, kid2.Id }.OrderBy(x => x), memberIds);
            Assert.DoesNotContain(assignments, a => a.FamilyMemberId == parent.Id);
        }
    }

    [Fact]
    public async Task Everyone_HitsEntireFamily()
    {
        var (ctx, parent, teen, kid1, kid2) = Family();
        using (ctx)
        {
            TestData.AddChore(ctx, "Family cleanup", assignToGroup: AssignToGroup.Everyone);

            var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

            var memberIds = assignments.Select(a => a.FamilyMemberId).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { parent.Id, teen.Id, kid1.Id, kid2.Id }.OrderBy(x => x), memberIds);
        }
    }

    [Fact]
    public async Task GroupChore_IsNotDoubleAssignedInRegularPass()
    {
        var (ctx, parent, teen, kid1, kid2) = Family();
        using (ctx)
        {
            // A group chore that is also Daily: it must fan out via the group pass ONLY,
            // and be excluded from the regular per-person distribution.
            TestData.AddChore(ctx, "Everyone dishes", frequency: ChoreFrequency.Daily,
                assignToGroup: AssignToGroup.Everyone);

            var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

            // Exactly one assignment per member for this chore - no duplicates.
            Assert.Equal(4, assignments.Count);
            var perMember = assignments.GroupBy(a => a.FamilyMemberId).Select(g => g.Count());
            Assert.All(perMember, count => Assert.Equal(1, count));
        }
    }
}