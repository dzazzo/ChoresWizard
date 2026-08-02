using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Zazzo.ChoresWizard2000.Data;
using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Controllers;

public class AssignmentsController : Controller
{
    private readonly ChoresDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _timeZone;

    public AssignmentsController(ChoresDbContext context, TimeProvider timeProvider, TimeZoneInfo timeZone)
    {
        _context = context;
        _timeProvider = timeProvider;
        _timeZone = timeZone;
    }

    public async Task<IActionResult> Index()
    {
        // Household-local month, not UTC (issue #3).
        var period = MonthPeriod.Current(_timeProvider, _timeZone);
        ViewData["CurrentMonthLabel"] = period.ToLabel();
        var assignments = await _context.ChoreAssignments
            .Include(ca => ca.FamilyMember)
            .Include(ca => ca.Chore)
            .Where(ca => ca.Month == period.Month && ca.Year == period.Year)
            .OrderBy(ca => ca.FamilyMember!.Name)
            .ThenBy(ca => ca.Chore!.Name)
            .ToListAsync();

        return View(assignments);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var assignment = await _context.ChoreAssignments
            .Include(ca => ca.FamilyMember)
            .Include(ca => ca.Chore)
            .FirstOrDefaultAsync(ca => ca.Id == id);

        if (assignment == null) return NotFound();

        ViewBag.FamilyMembers = new SelectList(
            await _context.FamilyMembers.Where(fm => fm.IsActive).ToListAsync(),
            "Id",
            "Name",
            assignment.FamilyMemberId);

        return View(assignment);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, int FamilyMemberId)
    {
        var assignment = await _context.ChoreAssignments.FindAsync(id);
        if (assignment == null) return NotFound();

        assignment.FamilyMemberId = FamilyMemberId;
        // Persisted timestamps stay in UTC; only month determination is localized.
        assignment.AssignedDate = _timeProvider.GetUtcNow().UtcDateTime; // Update the assignment date

        await _context.SaveChangesAsync();
        
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var assignment = await _context.ChoreAssignments.FindAsync(id);
        if (assignment != null)
        {
            _context.ChoreAssignments.Remove(assignment);
            await _context.SaveChangesAsync();
        }
        
        return RedirectToAction(nameof(Index));
    }
}
