using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Zazzo.ChoresWizard2000.Data;
using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Controllers;

public class ChoresController : Controller
{
    private readonly ChoresDbContext _context;

    public ChoresController(ChoresDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var chores = await _context.Chores
            .Include(c => c.PinnedToFamilyMember)
            .ToListAsync();
        return View(chores);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateFamilyMembersDropdown();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Chore chore)
    {
        if (ModelState.IsValid)
        {
            _context.Add(chore);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        await PopulateFamilyMembersDropdown(chore.PinnedToFamilyMemberId);
        return View(chore);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var chore = await _context.Chores.FindAsync(id);
        if (chore == null) return NotFound();

        await PopulateFamilyMembersDropdown(chore.PinnedToFamilyMemberId);
        return View(chore);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Chore chore)
    {
        if (id != chore.Id) return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(chore);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await ChoreExists(chore.Id))
                    return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Index));
        }
        await PopulateFamilyMembersDropdown(chore.PinnedToFamilyMemberId);
        return View(chore);
    }

    private async Task PopulateFamilyMembersDropdown(int? selectedId = null)
    {
        var familyMembers = await _context.FamilyMembers
            .Where(fm => fm.IsActive)
            .OrderBy(fm => fm.Name)
            .ToListAsync();

        var items = new List<SelectListItem>
        {
            new SelectListItem { Value = "", Text = "-- Not Pinned (Random Assignment) --" }
        };
        items.AddRange(familyMembers.Select(fm => new SelectListItem
        {
            Value = fm.Id.ToString(),
            Text = fm.Name,
            Selected = fm.Id == selectedId
        }));

        ViewBag.FamilyMembers = items;
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var chore = await _context.Chores
            .FirstOrDefaultAsync(m => m.Id == id);
        if (chore == null) return NotFound();

        return View(chore);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var chore = await _context.Chores.FindAsync(id);
        if (chore != null)
        {
            // Delete related assignments first
            var relatedAssignments = await _context.ChoreAssignments
                .Where(ca => ca.ChoreId == id)
                .ToListAsync();
            _context.ChoreAssignments.RemoveRange(relatedAssignments);
            
            _context.Chores.Remove(chore);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> ChoreExists(int id)
    {
        return await _context.Chores.AnyAsync(e => e.Id == id);
    }
}
