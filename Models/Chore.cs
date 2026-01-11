namespace Zazzo.ChoresWizard2000.Models;

public class Chore
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public ChoreFrequency Frequency { get; set; }
    public AgeRestriction AgeRestriction { get; set; } = AgeRestriction.Everyone;
    public bool IsActive { get; set; } = true;
    
    // Pin this chore to always be assigned to a specific person
    public int? PinnedToFamilyMemberId { get; set; }
    public FamilyMember? PinnedToFamilyMember { get; set; }
    
    // Assign this chore to ALL members of a group (e.g., all kids get "clean your room")
    public AssignToGroup AssignToGroup { get; set; } = AssignToGroup.OnePersonOnly;
}
