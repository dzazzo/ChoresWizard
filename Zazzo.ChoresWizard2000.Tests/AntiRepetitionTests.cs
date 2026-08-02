using Xunit;
using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Tests;

public class AntiRepetitionTests
{
    private const int Year = 2025;
    private const int Month = 6;
    // Previous month is 5/2025 given the current (June) run.
    private const int PrevYear = 2025;
    private const int PrevMonth = 5;

    [Fact]
    public async Task MemberWhoHadChoreLastMonth_IsDeprioritised()
    {
        using var ctx = TestDbContext.Create();
        var memberA = TestData.AddMember(ctx, "A", isParent: true);
        var memberB = TestData.AddMember(ctx, "B", isParent: true);
        var chore = TestData.AddChore(ctx, "Vacuum");

        // A had this exact chore last month -> A is excluded from the preferred pool,
        // leaving B as the only candidate.
        TestData.AddPreviousAssignment(ctx, memberA.Id, chore.Id, PrevYear, PrevMonth);

        var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

        var single = Assert.Single(assignments);
        Assert.Equal(memberB.Id, single.FamilyMemberId);
    }

    [Fact]
    public async Task WhenEveryEligibleMemberHadItLastMonth_ChoreIsStillAssigned()
    {
        using var ctx = TestDbContext.Create();
        // Only one eligible member.
        var memberA = TestData.AddMember(ctx, "A", isParent: true);
        var chore = TestData.AddChore(ctx, "Scrub floors");

        // The sole eligible member had it last month; the preferred pool is empty, so
        // the algorithm must fall back to the full eligible pool rather than drop it.
        TestData.AddPreviousAssignment(ctx, memberA.Id, chore.Id, PrevYear, PrevMonth);

        var assignments = await TestData.CreateService(ctx).DistributeChoresAsync(Year, Month);

        var single = Assert.Single(assignments);
        Assert.Equal(memberA.Id, single.FamilyMemberId);
        Assert.Equal(chore.Id, single.ChoreId);
    }
}