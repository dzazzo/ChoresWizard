using System.Globalization;

namespace Zazzo.ChoresWizard2000.Models;

/// <summary>
/// A single calendar month (year + month), the unit the app assigns chores by.
/// This is the one place month reasoning lives, so callers never scatter
/// <c>.Year</c>/<c>.Month</c> arithmetic (or, worse, derive the month from a raw
/// <see cref="DateTime.UtcNow"/>) across controllers and services.
///
/// The type itself is timezone-agnostic: it is just (year, month). Turning an
/// <em>instant</em> into a month, or a month into its local start/end instants,
/// requires a <see cref="TimeZoneInfo"/> and is done through the methods that take
/// one. This keeps household-local reasoning explicit at every call site.
///
/// It is deliberately shaped for the two downstream features built on top of it:
///   * auto-resort (#5) fires on the last local day of the month and generates the
///     next month — see <see cref="IsLastDay"/> and <see cref="Next"/>;
///   * export (#9) states a span of the 1st through the true last day — see
///     <see cref="FirstDay"/> and <see cref="LastDay"/>.
/// </summary>
public readonly record struct MonthPeriod
{
    public int Year { get; }

    public int Month { get; }

    public MonthPeriod(int year, int month)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(
                nameof(month), month, "Month must be between 1 and 12.");
        }

        if (year is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(
                nameof(year), year, "Year must be between 1 and 9999.");
        }

        Year = year;
        Month = month;
    }

    /// <summary>Number of days in this month (28-31, leap years handled).</summary>
    public int DaysInMonth => DateTime.DaysInMonth(Year, Month);

    /// <summary>The 1st of the month.</summary>
    public DateOnly FirstDay => new(Year, Month, 1);

    /// <summary>The true last day of the month (28/29/30/31).</summary>
    public DateOnly LastDay => new(Year, Month, DaysInMonth);

    /// <summary>The month immediately after this one, rolling the year over at December.</summary>
    public MonthPeriod Next() => Month == 12
        ? new MonthPeriod(Year + 1, 1)
        : new MonthPeriod(Year, Month + 1);

    /// <summary>The month immediately before this one, rolling the year back at January.</summary>
    public MonthPeriod Previous() => Month == 1
        ? new MonthPeriod(Year - 1, 12)
        : new MonthPeriod(Year, Month - 1);

    /// <summary>True when <paramref name="date"/> falls within this calendar month.</summary>
    public bool Contains(DateOnly date) => date.Year == Year && date.Month == Month;

    /// <summary>
    /// True when <paramref name="instant"/>, viewed in <paramref name="timeZone"/>,
    /// falls within this calendar month. This is the timezone-correct answer to
    /// "does this UTC timestamp belong to this month for the household?".
    /// </summary>
    public bool Contains(DateTimeOffset instant, TimeZoneInfo timeZone)
        => FromInstant(instant, timeZone) == this;

    /// <summary>
    /// True when <paramref name="date"/> is the last day of this month. The core
    /// question auto-resort (#5) asks each day, and correct across DST because it is
    /// purely calendar arithmetic on a local date.
    /// </summary>
    public bool IsLastDay(DateOnly date) => Contains(date) && date.Day == DaysInMonth;

    /// <summary>
    /// The instant this month begins in <paramref name="timeZone"/> — local midnight
    /// on the 1st, carrying that day's UTC offset so DST is reflected correctly.
    /// </summary>
    public DateTimeOffset StartLocal(TimeZoneInfo timeZone) => LocalMidnight(FirstDay, timeZone);

    /// <summary>
    /// The exclusive end instant of this month in <paramref name="timeZone"/> — local
    /// midnight on the 1st of the following month. The half-open interval
    /// [StartLocal, EndLocalExclusive) is the set of instants belonging to this month.
    /// </summary>
    public DateTimeOffset EndLocalExclusive(TimeZoneInfo timeZone) => Next().StartLocal(timeZone);

    /// <summary>
    /// The month that <paramref name="instant"/> belongs to when viewed in
    /// <paramref name="timeZone"/>. This is the replacement for reading
    /// <c>DateTime.UtcNow.Month</c>: the instant is projected into household-local
    /// time first, so 5&#160;PM Pacific on Jan&#160;31 resolves to January even though
    /// it is already Feb&#160;1 in UTC. DST is handled by <see cref="TimeZoneInfo.ConvertTime(DateTimeOffset, TimeZoneInfo)"/>.
    /// </summary>
    public static MonthPeriod FromInstant(DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var local = TimeZoneInfo.ConvertTime(instant, timeZone);
        return new MonthPeriod(local.Year, local.Month);
    }

    /// <summary>The month containing <paramref name="date"/>.</summary>
    public static MonthPeriod FromDate(DateOnly date) => new(date.Year, date.Month);

    /// <summary>
    /// The current household month: <paramref name="timeProvider"/>'s current instant
    /// projected into <paramref name="timeZone"/>. The single entry point for
    /// "what month is it right now" used across controllers and services.
    /// </summary>
    public static MonthPeriod Current(TimeProvider timeProvider, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return FromInstant(timeProvider.GetUtcNow(), timeZone);
    }

    /// <summary>Human-readable label such as "January 2025", for view headers.</summary>
    public string ToLabel(string format = "MMMM yyyy")
        => FirstDay.ToString(format, CultureInfo.CurrentCulture);

    public override string ToString() => $"{Year:D4}-{Month:D2}";

    // Builds the DateTimeOffset for local midnight on the given day. The offset is
    // computed for that specific wall-clock time so it reflects DST. Month boundaries
    // fall on midnight of the 1st, which is never a DST transition instant in the US,
    // so there is no ambiguous/invalid-time hazard here.
    private static DateTimeOffset LocalMidnight(DateOnly day, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var localMidnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = timeZone.GetUtcOffset(localMidnight);
        return new DateTimeOffset(localMidnight, offset);
    }
}
