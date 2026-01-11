using Microsoft.EntityFrameworkCore;
using Zazzo.ChoresWizard2000.Data;
using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Services;

public class SortingHatService
{
    private readonly ChoresDbContext _context;
    private readonly Random _random = new();

    public SortingHatService(ChoresDbContext context)
    {
        _context = context;
    }

    public async Task<List<ChoreAssignment>> DistributeChoresAsync(int year, int month)
    {
        var familyMembers = await _context.FamilyMembers
            .Where(fm => fm.IsActive)
            .ToListAsync();

        var chores = await _context.Chores
            .Where(c => c.IsActive)
            .ToListAsync();

        // Get previous month's assignments to avoid repetition
        var previousMonth = month == 1 ? 12 : month - 1;
        var previousYear = month == 1 ? year - 1 : year;
        
        var previousAssignments = await _context.ChoreAssignments
            .Where(ca => ca.Month == previousMonth && ca.Year == previousYear)
            .ToListAsync();

        var dailyChores = chores.Where(c => c.Frequency == ChoreFrequency.Daily).ToList();
        var weeklyChores = chores.Where(c => c.Frequency == ChoreFrequency.Weekly).ToList();

        var newAssignments = new List<ChoreAssignment>();

        // First, handle group assignments (chores that go to ALL members of a group)
        var groupChores = chores.Where(c => c.AssignToGroup != AssignToGroup.OnePersonOnly).ToList();
        newAssignments.AddRange(CreateGroupAssignments(groupChores, familyMembers, year, month));

        // Filter out group chores from regular distribution
        var regularDailyChores = dailyChores.Where(c => c.AssignToGroup == AssignToGroup.OnePersonOnly).ToList();
        var regularWeeklyChores = weeklyChores.Where(c => c.AssignToGroup == AssignToGroup.OnePersonOnly).ToList();

        // Distribute daily chores (2-3 per person)
        newAssignments.AddRange(DistributeChoresByFrequency(
            regularDailyChores, familyMembers, previousAssignments, year, month, 2, 3));

        // Distribute weekly chores (1-2 per person)
        newAssignments.AddRange(DistributeChoresByFrequency(
            regularWeeklyChores, familyMembers, previousAssignments, year, month, 1, 2));

        // Save assignments
        await _context.ChoreAssignments.AddRangeAsync(newAssignments);
        await _context.SaveChangesAsync();

        return newAssignments;
    }

    private List<ChoreAssignment> DistributeChoresByFrequency(
        List<Chore> chores,
        List<FamilyMember> familyMembers,
        List<ChoreAssignment> previousAssignments,
        int year,
        int month,
        int minPerPerson,
        int maxPerPerson)
    {
        var assignments = new List<ChoreAssignment>();
        var memberChoreCount = familyMembers.ToDictionary(fm => fm.Id, _ => 0);
        var memberCategoryCount = new Dictionary<int, Dictionary<string, int>>();
        
        // Initialize category tracking for each member
        foreach (var member in familyMembers)
        {
            memberCategoryCount[member.Id] = new Dictionary<string, int>();
        }

        // Shuffle chores for randomness, but process pinned chores first
        var pinnedChores = chores.Where(c => c.PinnedToFamilyMemberId.HasValue).ToList();
        var unpinnedChores = chores.Where(c => !c.PinnedToFamilyMemberId.HasValue).OrderBy(_ => _random.Next()).ToList();
        var orderedChores = pinnedChores.Concat(unpinnedChores).ToList();

        foreach (var chore in orderedChores)
        {
            // If chore is pinned, assign directly to the pinned family member
            if (chore.PinnedToFamilyMemberId.HasValue)
            {
                var pinnedMember = familyMembers.FirstOrDefault(fm => fm.Id == chore.PinnedToFamilyMemberId.Value);
                if (pinnedMember != null)
                {
                    assignments.Add(new ChoreAssignment
                    {
                        FamilyMemberId = pinnedMember.Id,
                        ChoreId = chore.Id,
                        AssignedDate = DateTime.Now,
                        Month = month,
                        Year = year
                    });
                    memberChoreCount[pinnedMember.Id]++;
                    
                    if (!string.IsNullOrWhiteSpace(chore.Category))
                    {
                        if (!memberCategoryCount[pinnedMember.Id].ContainsKey(chore.Category))
                        {
                            memberCategoryCount[pinnedMember.Id][chore.Category] = 0;
                        }
                        memberCategoryCount[pinnedMember.Id][chore.Category]++;
                    }
                    continue;
                }
            }

            // Filter eligible members based on age restrictions
            var eligibleMembers = GetEligibleMembers(chore, familyMembers);

            // Filter out members who had this chore last month (if possible)
            var preferredMembers = eligibleMembers
                .Where(fm => !previousAssignments
                    .Any(pa => pa.FamilyMemberId == fm.Id && pa.ChoreId == chore.Id))
                .ToList();

            // If no preferred members, fall back to all eligible
            var candidatePool = preferredMembers.Any() ? preferredMembers : eligibleMembers;

            // Find members who haven't reached their quota AND category limit
            var availableMembers = candidatePool
                .Where(fm => memberChoreCount[fm.Id] < maxPerPerson)
                .Where(fm => {
                    // If chore has a category, check if member already has one from this category
                    if (!string.IsNullOrWhiteSpace(chore.Category))
                    {
                        return !memberCategoryCount[fm.Id].ContainsKey(chore.Category) || 
                               memberCategoryCount[fm.Id][chore.Category] == 0;
                    }
                    return true;
                })
                .ToList();

            if (!availableMembers.Any())
            {
                // If no one meets category constraint, try without it (but respect quota)
                availableMembers = candidatePool
                    .Where(fm => memberChoreCount[fm.Id] < maxPerPerson)
                    .ToList();
            }

            if (!availableMembers.Any())
            {
                // If everyone is at max, assign to random eligible member
                availableMembers = candidatePool.ToList();
            }

            // Select random member from available pool
            var selectedMember = availableMembers[_random.Next(availableMembers.Count)];
            
            assignments.Add(new ChoreAssignment
            {
                FamilyMemberId = selectedMember.Id,
                ChoreId = chore.Id,
                AssignedDate = DateTime.Now,
                Month = month,
                Year = year
            });

            memberChoreCount[selectedMember.Id]++;
            
            // Track category assignment
            if (!string.IsNullOrWhiteSpace(chore.Category))
            {
                if (!memberCategoryCount[selectedMember.Id].ContainsKey(chore.Category))
                {
                    memberCategoryCount[selectedMember.Id][chore.Category] = 0;
                }
                memberCategoryCount[selectedMember.Id][chore.Category]++;
            }
        }

        // Ensure minimum assignments (if there are enough chores)
        BalanceAssignments(assignments, familyMembers, chores, year, month, minPerPerson, memberChoreCount);

        return assignments;
    }

    private List<ChoreAssignment> CreateGroupAssignments(
        List<Chore> groupChores,
        List<FamilyMember> familyMembers,
        int year,
        int month)
    {
        var assignments = new List<ChoreAssignment>();

        foreach (var chore in groupChores)
        {
            var targetMembers = GetGroupMembers(chore.AssignToGroup, familyMembers);
            
            foreach (var member in targetMembers)
            {
                assignments.Add(new ChoreAssignment
                {
                    FamilyMemberId = member.Id,
                    ChoreId = chore.Id,
                    AssignedDate = DateTime.Now,
                    Month = month,
                    Year = year
                });
            }
        }

        return assignments;
    }

    private List<FamilyMember> GetGroupMembers(AssignToGroup group, List<FamilyMember> familyMembers)
    {
        return group switch
        {
            AssignToGroup.AllKids => familyMembers.Where(fm => !fm.IsParent && !fm.IsTeen).ToList(),
            AssignToGroup.AllTeens => familyMembers.Where(fm => fm.IsTeen).ToList(),
            AssignToGroup.AllChildren => familyMembers.Where(fm => !fm.IsParent).ToList(),
            AssignToGroup.AllAdults => familyMembers.Where(fm => fm.IsParent).ToList(),
            AssignToGroup.Everyone => familyMembers.ToList(),
            _ => new List<FamilyMember>()
        };
    }

    private List<FamilyMember> GetEligibleMembers(Chore chore, List<FamilyMember> familyMembers)
    {
        return chore.AgeRestriction switch
        {
            AgeRestriction.AdultsOnly => familyMembers.Where(fm => fm.IsParent).ToList(),
            AgeRestriction.TeensAndAdults => familyMembers.Where(fm => fm.IsParent || fm.IsTeen).ToList(),
            _ => familyMembers.ToList() // Everyone
        };
    }

    private void BalanceAssignments(
        List<ChoreAssignment> assignments,
        List<FamilyMember> familyMembers,
        List<Chore> allChores,
        int year,
        int month,
        int minPerPerson,
        Dictionary<int, int> memberChoreCount)
    {
        // This is a simple balancing - could be enhanced with more sophisticated logic
        foreach (var member in familyMembers)
        {
            if (memberChoreCount[member.Id] < minPerPerson)
            {
                var neededChores = minPerPerson - memberChoreCount[member.Id];
                // Note: In a real scenario, you'd want more sophisticated balancing
                // This is a placeholder for the minimum logic
            }
        }
    }

    public async Task<List<ChoreAssignment>> GetCurrentMonthAssignmentsAsync()
    {
        var now = DateTime.Now;
        return await _context.ChoreAssignments
            .Include(ca => ca.FamilyMember)
            .Include(ca => ca.Chore)
            .Where(ca => ca.Month == now.Month && ca.Year == now.Year)
            .OrderBy(ca => ca.FamilyMember!.Name)
            .ThenBy(ca => ca.Chore!.Name)
            .ToListAsync();
    }

    public async Task<bool> ClearAssignmentsAsync(int year, int month)
    {
        var assignments = await _context.ChoreAssignments
            .Where(ca => ca.Month == month && ca.Year == year)
            .ToListAsync();

        if (assignments.Any())
        {
            _context.ChoreAssignments.RemoveRange(assignments);
            await _context.SaveChangesAsync();
            return true;
        }

        return false;
    }
}
