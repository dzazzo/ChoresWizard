using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Zazzo.ChoresWizard2000.Models;
using Zazzo.ChoresWizard2000.Services;

namespace Zazzo.ChoresWizard2000.Controllers;

public class HomeController : Controller
{
    private readonly SortingHatService _sortingHatService;
    private readonly ILogger<HomeController> _logger;

    public HomeController(SortingHatService sortingHatService, ILogger<HomeController> logger)
    {
        _sortingHatService = sortingHatService;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        try
        {
            ViewData["CurrentMonthLabel"] = _sortingHatService.GetCurrentMonth().ToLabel();
            var assignments = await _sortingHatService.GetCurrentMonthAssignmentsAsync();
            return View(assignments);
        }
        catch (Exception ex)
        {
            // A database hiccup (e.g. Azure SQL waking from a cold start) must not
            // surface as an unhandled 500. Render the homepage with an empty model
            // plus a friendly banner so the site stays usable and self-explains.
            _logger.LogError(ex, "Failed to load current month assignments for the homepage.");
            ViewData["DatabaseUnavailable"] = true;
            return View(new List<ChoreAssignment>());
        }
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
