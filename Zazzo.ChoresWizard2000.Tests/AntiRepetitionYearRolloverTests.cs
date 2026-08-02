using Xunit;
using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Tests;

/// <summary>
/// Anti-repetition across the December -> January boundary.
///
/// <see cref="AntiRepetitionTests"/> covers the ordinary within-year case (May -> June).
/// January is the one month where "last month" also requires decrementing the *year*:
///
///     var previousMonth = month == 1 ? 12 : month - 1;
///     var previousYear  = month == 1 ? year - 1 : year;
///
/// If that year decrement were ever dropped, a January sort would look up December of the
/// *current* year — a month that does not exist yet — find nothing, and silently lose
/// anti-repetition for one month a year. Nothing else in the suite would fail, which is
/// exactly why this is pinned here.
/// </summary>
public class AntiRepetitionYearRolloverTests
{
    private const int JanuaryYear = 2025;
    private const int January = 1;

    // The correct look-back target for a January 2025 sort.
    private const int DecemberYear = 2024;
    private const int December = 12;

    [Fact]
    public async Task ChoreAssignedInDecember_IsNotRepeatedInJanuary()
    {
        using var ctx = TestDbContext.Create();
        var memberA = TestData.AddMember(ctx, "A", isParent: true);
        var memberB = TestData.AddMember(ctx, "B", isParent: true);
        var chore = TestData.AddChore(ctx, "Take out trash");

        // A had this exact chore in December 2024.
        TestData.AddPreviousAssignment(ctx, memberA.Id, chore.Id, DecemberYear, December);

        var assignments = await TestData.CreateService(ctx)
            .DistributeChoresAsync(JanuaryYear, January);

        // A is excluded from the preferred pool, leaving B as the only candidate.
        // This holds for every seed, so the assertion is not luck.
        var single = Assert.Single(assignments);
        Assert.Equal(memberB.Id, single.FamilyMemberId);
    }

    [Fact]
    public async Task JanuarySort_LooksAtDecemberOfPreviousYear_NotDecemberOfCurrentYear()
    {
        // Discriminator for the year decrement specifically.
        //
        // Here the only stored assignment is December *2025* — same calendar month, wrong
        // year, and precisely what a buggy `previousYear = year` would query for a
        // January 2025 sort. It must be ignored, so A stays eligible and can still win.
        //
        // With both members eligible the winner is random, so a single run proves nothing.
        // Sweeping fixed seeds makes it deterministic and repeatable: if the wrong-year
        // record were honoured, A would be excluded and B would win every single seed.
        var winners = new List<int>();

        foreach (var seed in Enumerable.Range(1, 25))
        {
            using var ctx = TestDbContext.Create();
            var memberA = TestData.AddMember(ctx, "A", isParent: true);
            TestData.AddMember(ctx, "B", isParent: true);
            var chore = TestData.AddChore(ctx, "Take out trash");

            TestData.AddPreviousAssignment(ctx, memberA.Id, chore.Id, JanuaryYear, December);

            var assignments = await TestData.CreateService(ctx, seed)
                .DistributeChoresAsync(JanuaryYear, January);

            var single = Assert.Single(assignments);
            if (single.FamilyMemberId == memberA.Id)
            {
                winners.Add(seed);
            }
        }

        Assert.NotEmpty(winners);
    }

    [Fact]
    public async Task WhenEveryEligibleMemberHadItInDecember_ChoreIsStillAssignedInJanuary()
    {
        using var ctx = TestDbContext.Create();
        var memberA = TestData.AddMember(ctx, "A", isParent: true);
        var chore = TestData.AddChore(ctx, "Scrub floors");

        // The sole eligible member had it in December. The preferred pool empties, so the
        // algorithm must fall back to the full eligible pool rather than drop the chore.
        // A chore silently going undone is a worse outcome than a repeat.
        TestData.AddPreviousAssignment(ctx, memberA.Id, chore.Id, DecemberYear, December);

        var assignments = await TestData.CreateService(ctx)
            .DistributeChoresAsync(JanuaryYear, January);

        var single = Assert.Single(assignments);
        Assert.Equal(memberA.Id, single.FamilyMemberId);
        Assert.Equal(chore.Id, single.ChoreId);
    }
}
