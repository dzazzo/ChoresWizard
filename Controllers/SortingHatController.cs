using Microsoft.AspNetCore.Mvc;
using Zazzo.ChoresWizard2000.Services;

namespace Zazzo.ChoresWizard2000.Controllers;

public class SortingHatController : Controller
{
    private readonly SortingHatService _sortingHatService;

    public SortingHatController(SortingHatService sortingHatService)
    {
        _sortingHatService = sortingHatService;
    }

    public async Task<IActionResult> Index()
    {
        var assignments = await _sortingHatService.GetCurrentMonthAssignmentsAsync();
        return View(assignments);
    }

    public IActionResult Ceremony()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Sort()
    {
        var now = DateTime.UtcNow;
        
        // Check if assignments already exist for this month
        var existingAssignments = await _sortingHatService.GetCurrentMonthAssignmentsAsync();
        if (existingAssignments.Any())
        {
            TempData["Error"] = "Assignments already exist for this month. Clear them first to re-sort.";
            return RedirectToAction(nameof(Results));
        }
        
        await _sortingHatService.DistributeChoresAsync(now.Year, now.Month);
        return RedirectToAction(nameof(Results));
    }

    public async Task<IActionResult> Results()
    {
        var assignments = await _sortingHatService.GetCurrentMonthAssignmentsAsync();
        return View(assignments);
    }

    [HttpPost]
    public async Task<IActionResult> ClearCurrentMonth()
    {
        var now = DateTime.UtcNow;
        await _sortingHatService.ClearAssignmentsAsync(now.Year, now.Month);
        return RedirectToAction(nameof(Index));
    }
}
