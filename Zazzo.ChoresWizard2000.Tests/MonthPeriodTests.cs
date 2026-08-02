using Xunit;
using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Tests;

/// <summary>
/// Unit tests for the <see cref="MonthPeriod"/> value type — the single place month
/// reasoning lives. These are pure calendar assertions with no clock or DB dependence.
/// </summary>
public class MonthPeriodTests
{
    [Theory]
    [InlineData(2025, 1, 31)]
    [InlineData(2025, 2, 28)]  // common year
    [InlineData(2024, 2, 29)]  // leap year
    [InlineData(2000, 2, 29)]  // century leap year
    [InlineData(1900, 2, 28)]  // century non-leap year
    [InlineData(2025, 4, 30)]
    [InlineData(2025, 12, 31)]
    public void DaysInMonth_HandlesLeapYears(int year, int month, int expected)
    {
        Assert.Equal(expected, new MonthPeriod(year, month).DaysInMonth);
    }

    [Fact]
    public void FirstDay_And_LastDay_BoundTheMonth()
    {
        var february = new MonthPeriod(2024, 2);
        Assert.Equal(new DateOnly(2024, 2, 1), february.FirstDay);
        Assert.Equal(new DateOnly(2024, 2, 29), february.LastDay);
    }

    [Fact]
    public void Next_RollsOverTheYearAtDecember()
    {
        Assert.Equal(new MonthPeriod(2026, 1), new MonthPeriod(2025, 12).Next());
        Assert.Equal(new MonthPeriod(2025, 7), new MonthPeriod(2025, 6).Next());
    }

    [Fact]
    public void Previous_RollsBackTheYearAtJanuary()
    {
        Assert.Equal(new MonthPeriod(2024, 12), new MonthPeriod(2025, 1).Previous());
        Assert.Equal(new MonthPeriod(2025, 5), new MonthPeriod(2025, 6).Previous());
    }

    [Fact]
    public void Contains_DateOnly_IsTrueOnlyInsideTheMonth()
    {
        var june = new MonthPeriod(2025, 6);
        Assert.True(june.Contains(new DateOnly(2025, 6, 1)));
        Assert.True(june.Contains(new DateOnly(2025, 6, 30)));
        Assert.False(june.Contains(new DateOnly(2025, 5, 31)));
        Assert.False(june.Contains(new DateOnly(2025, 7, 1)));
        Assert.False(june.Contains(new DateOnly(2024, 6, 15)));
    }

    [Fact]
    public void IsLastDay_IsTrueOnlyOnTheFinalDay()
    {
        var february2024 = new MonthPeriod(2024, 2);
        Assert.True(february2024.IsLastDay(new DateOnly(2024, 2, 29)));
        Assert.False(february2024.IsLastDay(new DateOnly(2024, 2, 28)));
        // A date in a different month is never "the last day of this month".
        Assert.False(february2024.IsLastDay(new DateOnly(2024, 3, 31)));
    }

    [Fact]
    public void IsLastDay_February28_IsLastInCommonYear_NotInLeapYear()
    {
        Assert.True(new MonthPeriod(2025, 2).IsLastDay(new DateOnly(2025, 2, 28)));
        Assert.False(new MonthPeriod(2024, 2).IsLastDay(new DateOnly(2024, 2, 28)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void Constructor_RejectsInvalidMonth(int month)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonthPeriod(2025, month));
    }

    [Fact]
    public void ToLabel_ProducesHumanReadableMonthYear()
    {
        Assert.Equal("January 2025", new MonthPeriod(2025, 1).ToLabel());
    }

    [Fact]
    public void ToString_IsSortableYearMonth()
    {
        Assert.Equal("2025-03", new MonthPeriod(2025, 3).ToString());
    }

    [Fact]
    public void FromDate_ProjectsToItsContainingMonth()
    {
        Assert.Equal(new MonthPeriod(2025, 3), MonthPeriod.FromDate(new DateOnly(2025, 3, 17)));
    }
}
