namespace Zazzo.ChoresWizard2000.Models;

public class FamilyMember
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public bool IsParent { get; set; }
    public bool IsTeen { get; set; }
    public bool IsActive { get; set; } = true;
}
