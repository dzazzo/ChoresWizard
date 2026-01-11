namespace Zazzo.ChoresWizard2000.Models;

/// <summary>
/// Defines groups of family members who should ALL receive this chore
/// </summary>
public enum AssignToGroup
{
    /// <summary>Normal distribution - assign to one person based on algorithm</summary>
    OnePersonOnly,
    
    /// <summary>Assign to all kids (not teens, not parents)</summary>
    AllKids,
    
    /// <summary>Assign to all teens</summary>
    AllTeens,
    
    /// <summary>Assign to all children (kids and teens, not parents)</summary>
    AllChildren,
    
    /// <summary>Assign to all parents/adults</summary>
    AllAdults,
    
    /// <summary>Assign to everyone in the family</summary>
    Everyone
}
