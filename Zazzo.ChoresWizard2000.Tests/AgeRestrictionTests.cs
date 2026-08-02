using Xunit;
using Zazzo.ChoresWizard2000.Models;
using Zazzo.ChoresWizard2000.Tests;

namespace Zazzo.ChoresWizard2000.Tests;

public class AgeRestrictionTests
{
    private const int Year = 2025;
    private const int Month = 6;

    [Fact]
    public async Task AdultsOnly_NeverAssignedToNonParent()
    {
        using var ctx = TestDbContext.Create();
        var parent = TestData.AddMember(ctx, "Parent", isParent: true);
        var teen = TestData.AddMember(ctx, "Teen", isTeen: true);
        var kid = TestData.AddMember(ctx, "Kid");
        TestData.AddChore(ctx, "Pay bills", ageRestriction: AgeRestriction.AdultsOnly);

        var service = TestData.CreateService(ctx);
        var assignments = await service.DistributeChoresAsync(Year, Month);

        var single = Assert.Single(assignments);
        Assert.Equal(parent.Id, single.FamilyMemberId);
        Assert.DoesNotContain(assignments, a => a.FamilyMemberId == teen.Id || a.FamilyMemberId == kid.Id);
    }

    [Fact]
    public async Task TeensAndAdults_NeverAssignedToYoungChild()
    {
        using var ctx = TestDbContext.Create();
        var parent = TestData.AddMember(ctx, "Parent", isParent: true);
        var teen = TestData.AddMember(ctx, "Teen", isTeen: true);
        var kid = TestData.AddMember(ctx, "Kid");
        TestData.AddChore(ctx, "Take out trash", ageRestriction: AgeRestriction.TeensAndAdults);

        var service = TestData.CreateService(ctx);
        var assignments = await service.DistributeChoresAsync(Year, Month);

        var single = Assert.Single(assignments);
        Assert.NotEqual(kid.Id, single.FamilyMemberId);
        Assert.Contains(single.FamilyMemberId, new[] { parent.Id, teen.Id });
    }

    [Fact]
    public async Task Everyone_IsUnrestricted()
    {
        using var ctx = TestDbContext.Create();
        var parent = TestData.AddMember(ctx, "Parent", isParent: true);
        var teen = TestData.AddMember(ctx, "Teen", isTeen: true);
        var kid = TestData.AddMember(ctx, "Kid");
        TestData.AddChore(ctx, "Feed the cat", ageRestriction: AgeRestriction.Everyone);

        var service = TestData.CreateService(ctx);
        var assignments = await service.DistributeChoresAsync(Year, Month);

        var single = Assert.Single(assignments);
        Assert.Contains(single.FamilyMemberId, new[] { parent.Id, teen.Id, kid.Id });
    }
}