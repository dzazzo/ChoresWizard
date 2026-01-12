using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zazzo.ChoresWizard2000.Data;
using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Controllers;

public class FamilyMembersController : Controller
{
    private readonly ChoresDbContext _context;

    public FamilyMembersController(ChoresDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var members = await _context.FamilyMembers.ToListAsync();
        return View(members);
    }

    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FamilyMember familyMember)
    {
        if (ModelState.IsValid)
        {
            _context.Add(familyMember);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(familyMember);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var familyMember = await _context.FamilyMembers.FindAsync(id);
        if (familyMember == null) return NotFound();

        return View(familyMember);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, FamilyMember familyMember)
    {
        if (id != familyMember.Id) return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(familyMember);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await FamilyMemberExists(familyMember.Id))
                    return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Index));
        }
        return View(familyMember);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var familyMember = await _context.FamilyMembers
            .FirstOrDefaultAsync(m => m.Id == id);
        if (familyMember == null) return NotFound();

        return View(familyMember);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var familyMember = await _context.FamilyMembers.FindAsync(id);
        if (familyMember != null)
        {
            // Delete related assignments first
            var relatedAssignments = await _context.ChoreAssignments
                .Where(ca => ca.FamilyMemberId == id)
                .ToListAsync();
            _context.ChoreAssignments.RemoveRange(relatedAssignments);
            
            // Also unpin any chores pinned to this member
            var pinnedChores = await _context.Chores
                .Where(c => c.PinnedToFamilyMemberId == id)
                .ToListAsync();
            foreach (var chore in pinnedChores)
            {
                chore.PinnedToFamilyMemberId = null;
            }
            
            _context.FamilyMembers.Remove(familyMember);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> FamilyMemberExists(int id)
    {
        return await _context.FamilyMembers.AnyAsync(e => e.Id == id);
    }
}
