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
        ViewData["CurrentMonthLabel"] = _sortingHatService.GetCurrentMonth().ToLabel();
        var assignments = await _sortingHatService.GetCurrentMonthAssignmentsAsync();
        return View(assignments);
    }

    public IActionResult Ceremony()
    {
        ViewData["CurrentMonthLabel"] = _sortingHatService.GetCurrentMonth().ToLabel();
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Sort()
    {
        // Determine the month from the household-local clock, not UTC (issue #3).
        var period = _sortingHatService.GetCurrentMonth();

        // Check if assignments already exist for this month
        var existingAssignments = await _sortingHatService.GetCurrentMonthAssignmentsAsync();
        if (existingAssignments.Any())
        {
            TempData["Error"] = "Assignments already exist for this month. Clear them first to re-sort.";
            return RedirectToAction(nameof(Results));
        }

        await _sortingHatService.DistributeChoresAsync(period.Year, period.Month);
        return RedirectToAction(nameof(Results));
    }

    public async Task<IActionResult> Results()
    {
        // Capture the period once and hand it to the view so the export buttons
        // request this exact month rather than re-resolving "current" on click.
        var period = _sortingHatService.GetCurrentMonth();
        ViewData["CurrentMonthLabel"] = period.ToLabel();
        ViewData["ExportPeriod"] = period;
        var assignments = await _sortingHatService.GetCurrentMonthAssignmentsAsync();
        return View(assignments);
    }

    [HttpPost]
    public async Task<IActionResult> ClearCurrentMonth()
    {
        var period = _sortingHatService.GetCurrentMonth();
        await _sortingHatService.ClearAssignmentsAsync(period.Year, period.Month);
        return RedirectToAction(nameof(Index));
    }
}
