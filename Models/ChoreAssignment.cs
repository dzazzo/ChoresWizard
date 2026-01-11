namespace Zazzo.ChoresWizard2000.Models;

public class ChoreAssignment
{
    public int Id { get; set; }
    public int FamilyMemberId { get; set; }
    public FamilyMember? FamilyMember { get; set; }
    public int ChoreId { get; set; }
    public Chore? Chore { get; set; }
    public DateTime AssignedDate { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
}
