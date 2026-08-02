using Xunit;
using Zazzo.ChoresWizard2000.Models;
using Zazzo.ChoresWizard2000.Models.Export;
using Zazzo.ChoresWizard2000.Services.Export;

namespace Zazzo.ChoresWizard2000.Tests;

/// <summary>
/// Tests for the CSV fallback export: header shape, the full-month span repeated on
/// every row (so the file is self-describing regardless of the run date), and
/// RFC 4180 quoting.
/// </summary>
public class ChoreCsvBuilderTests
{
    private static readonly MonthPeriod January2026 = new(2026, 1);

    [Fact]
    public void Build_EmitsHeaderRow()
    {
        var csv = ChoreCsvBuilder.Build(new MonthlyChoreExport(January2026, Array.Empty<ChoreExportItem>()));

        var firstLine = csv.Split("\r\n")[0];
        Assert.Equal("Member,Chore,Cadence,Category,MonthStart,MonthEnd", firstLine);
    }

    [Fact]
    public void Build_RepeatsFullMonthSpanOnEveryRow()
    {
        var items = new[]
        {
            new ChoreExportItem(1, 1, "Sam", "Dishes", ChoreFrequency.Daily, "Kitchen"),
            new ChoreExportItem(2, 2, "Alex", "Mow lawn", ChoreFrequency.Weekly, "Yard"),
        };

        var csv = ChoreCsvBuilder.Build(new MonthlyChoreExport(January2026, items));
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Sam,Dishes,Daily,Kitchen,2026-01-01,2026-01-31", lines[1]);
        Assert.Equal("Alex,Mow lawn,Weekly,Yard,2026-01-01,2026-01-31", lines[2]);
    }

    [Fact]
    public void Build_QuotesFieldsContainingCommas()
    {
        var items = new[]
        {
            new ChoreExportItem(1, 1, "Sam", "Wipe counters, table", ChoreFrequency.Daily, null),
        };

        var csv = ChoreCsvBuilder.Build(new MonthlyChoreExport(January2026, items));
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        // Comma-bearing field is quoted; a null category becomes an empty field.
        Assert.Equal("Sam,\"Wipe counters, table\",Daily,,2026-01-01,2026-01-31", lines[1]);
    }

    [Fact]
    public void Build_EscapesEmbeddedQuotes()
    {
        var items = new[]
        {
            new ChoreExportItem(1, 1, "Sam", "Clean \"guest\" room", ChoreFrequency.Daily, null),
        };

        var csv = ChoreCsvBuilder.Build(new MonthlyChoreExport(January2026, items));
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Sam,\"Clean \"\"guest\"\" room\",Daily,,2026-01-01,2026-01-31", lines[1]);
    }
}
