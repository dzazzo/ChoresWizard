using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Zazzo.ChoresWizard2000.Models;
using Zazzo.ChoresWizard2000.Services;

namespace Zazzo.ChoresWizard2000.Controllers;

public class HomeController : Controller
{
    private readonly SortingHatService _sortingHatService;

    public HomeController(SortingHatService sortingHatService)
    {
        _sortingHatService = sortingHatService;
    }

    public async Task<IActionResult> Index()
    {
        var assignments = await _sortingHatService.GetCurrentMonthAssignmentsAsync();
        return View(assignments);
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
