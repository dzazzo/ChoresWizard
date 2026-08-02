using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;
using Zazzo.ChoresWizard2000.Configuration;
using Zazzo.ChoresWizard2000.Controllers;
using Zazzo.ChoresWizard2000.Models;
using Zazzo.ChoresWizard2000.Services.Export;

namespace Zazzo.ChoresWizard2000.Tests;

/// <summary>
/// Behavioural tests for the export endpoints (issue #9): that the ICS feed is
/// reachable anonymously (it must bypass the global auth FallbackPolicy so Skylight
/// can subscribe), that the route token actually gates access, and that the exported
/// month is driven by <see cref="MonthPeriod"/> rather than the wall clock.
/// </summary>
public class ExportControllerTests
{
    private const string GoodToken = "s3cr3t-feed-token";

    private static ExportController CreateController(
        ChoresDbContextHolder holder,
        string? feedToken,
        FixedTimeProvider clock)
    {
        var service = new ChoreExportService(holder.Context, clock, TestData.Pacific);
        var options = Options.Create(new ExportOptions { FeedToken = feedToken });
        return new ExportController(service, options);
    }

    // A tiny helper so each test owns its context lifetime.
    private sealed class ChoresDbContextHolder : IDisposable
    {
        public TestDbContext Context { get; } = TestDbContext.Create();
        public void Dispose() => Context.Dispose();
    }

    [Fact]
    public void FeedAction_IsDecoratedForAnonymousAccess()
    {
        var method = typeof(ExportController).GetMethod(nameof(ExportController.Feed))!;

        // Without [AllowAnonymous] the global FallbackPolicy would force a login
        // redirect and Skylight could never subscribe.
        Assert.NotEmpty(method.GetCustomAttributes<AllowAnonymousAttribute>());

        var httpGet = method.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
        Assert.Equal("feed/{token}/chores.ics", httpGet!.Template);
    }

    [Fact]
    public void PdfAndCsvActions_AreNotAnonymous()
    {
        var pdf = typeof(ExportController).GetMethod(nameof(ExportController.Pdf))!;
        var csv = typeof(ExportController).GetMethod(nameof(ExportController.Csv))!;

        // These stay behind the default auth policy.
        Assert.Empty(pdf.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Empty(csv.GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Fact]
    public async Task Feed_WithCorrectToken_ReturnsCalendarFile()
    {
        using var holder = new ChoresDbContextHolder();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 20, 20, 0, 0, TimeSpan.Zero));
        SeedDailyChore(holder.Context);
        var controller = CreateController(holder, GoodToken, clock);

        var result = await controller.Feed(GoodToken, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/calendar; charset=utf-8", file.ContentType);
        Assert.NotEmpty(file.FileContents);
    }

    [Fact]
    public async Task Feed_WithWrongToken_Returns404()
    {
        using var holder = new ChoresDbContextHolder();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 20, 20, 0, 0, TimeSpan.Zero));
        var controller = CreateController(holder, GoodToken, clock);

        var result = await controller.Feed("not-the-token", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Feed_WhenNoTokenConfigured_Returns404(string? configuredToken)
    {
        using var holder = new ChoresDbContextHolder();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 20, 20, 0, 0, TimeSpan.Zero));
        var controller = CreateController(holder, configuredToken, clock);

        // Even an empty guessed token must not unlock an unconfigured feed.
        var result = await controller.Feed(configuredToken ?? "", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Feed_UsesFullMonthSpan_EvenWhenRunMidMonth()
    {
        using var holder = new ChoresDbContextHolder();
        // Clock is the 20th (mid-month); the feed must still span the whole month.
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 20, 20, 0, 0, TimeSpan.Zero));
        SeedDailyChore(holder.Context);
        var controller = CreateController(holder, GoodToken, clock);

        var result = await controller.Feed(GoodToken, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        var ics = System.Text.Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("DTSTART;VALUE=DATE:20260101", ics);
        Assert.Contains("UNTIL=20260131", ics);
    }

    [Fact]
    public async Task Csv_DefaultsToCurrentHouseholdMonth()
    {
        using var holder = new ChoresDbContextHolder();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 20, 20, 0, 0, TimeSpan.Zero));
        SeedDailyChore(holder.Context);
        var controller = CreateController(holder, GoodToken, clock);

        var result = await controller.Csv(year: null, month: null, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("chores-2026-01.csv", file.FileDownloadName);
    }

    [Fact]
    public async Task Pdf_RejectsInvalidMonth()
    {
        using var holder = new ChoresDbContextHolder();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 20, 20, 0, 0, TimeSpan.Zero));
        var controller = CreateController(holder, GoodToken, clock);

        var result = await controller.Pdf(year: 2026, month: 13, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Pdf_WithData_ReturnsPdfFile()
    {
        using var holder = new ChoresDbContextHolder();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 20, 20, 0, 0, TimeSpan.Zero));
        SeedDailyChore(holder.Context);
        var controller = CreateController(holder, GoodToken, clock);

        var result = await controller.Pdf(year: 2026, month: 1, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        // A real PDF starts with the %PDF- magic bytes.
        Assert.True(file.FileContents.Length > 4);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(file.FileContents, 0, 5));
    }

    private static void SeedDailyChore(TestDbContext context)
    {
        var member = TestData.AddMember(context, "Alex");
        var chore = TestData.AddChore(context, "Feed dog", ChoreFrequency.Daily, category: "Pets");
        TestData.AddPreviousAssignment(context, member.Id, chore.Id, 2026, 1);
    }
}
