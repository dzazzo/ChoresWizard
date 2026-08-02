using System.Collections.Generic;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Zazzo.ChoresWizard2000.Models;
using Zazzo.ChoresWizard2000.Models.Export;

namespace Zazzo.ChoresWizard2000.Services.Export;

/// <summary>
/// Builds an RFC 5545 iCalendar (ICS) feed of a month's chore assignments for
/// Skylight to subscribe to (issue #9). Pure and DB-free so recurrence shapes and
/// date boundaries can be pinned by unit tests.
///
/// Design decisions baked in here:
/// <list type="bullet">
///   <item>Entries are <b>all-day</b> events: <c>DTSTART;VALUE=DATE</c> with an
///   <b>exclusive</b> <c>DTEND</c> (first occurrence + 1 day). Getting that +1 right
///   is the classic ICS off-by-one; it is pinned by a test.</item>
///   <item><c>UNTIL</c> is the month's true last day and is <b>inclusive</b> per
///   RFC 5545. Because DTSTART is date-valued, Ical.Net serialises UNTIL as a DATE.</item>
///   <item>Skylight does not reliably map DESCRIPTION/CATEGORIES/ATTENDEE, so the
///   assignee and cadence are encoded in <c>SUMMARY</c>: <c>[ALEX] [DAILY] Feed dog</c>.</item>
///   <item>Each event carries a deterministic <c>UID</c> keyed on member + chore +
///   month, so re-publishing after a re-sort <b>updates</b> events rather than
///   duplicating them.</item>
/// </list>
/// </summary>
public static class SkylightIcsBuilder
{
    /// <summary>Default UID domain; overridable so the host name is not hardcoded.</summary>
    public const string DefaultUidDomain = "chores.zazzo.com";

    /// <summary>
    /// Serialises <paramref name="export"/> to ICS text.
    /// </summary>
    /// <param name="export">The month and its assignments.</param>
    /// <param name="generatedAt">
    /// The instant used for every <c>DTSTAMP</c>. Pass the injected
    /// <see cref="TimeProvider"/>'s value so output is deterministic under test.
    /// </param>
    /// <param name="uidDomain">Right-hand side of each UID; defaults to
    /// <see cref="DefaultUidDomain"/>.</param>
    public static string Build(
        MonthlyChoreExport export,
        DateTimeOffset generatedAt,
        string uidDomain = DefaultUidDomain)
    {
        ArgumentNullException.ThrowIfNull(export);

        var calendar = new Calendar();
        calendar.AddProperty("X-WR-CALNAME", $"Zazzo Chores — {export.Period.ToLabel()}");
        calendar.AddProperty("X-WR-CALDESC",
            $"Chore assignments for {export.Period.FirstDay:MMMM d} – {export.Period.LastDay:MMMM d, yyyy}.");

        var stamp = new CalDateTime(generatedAt.UtcDateTime, "UTC", hasTime: true);

        // Guard against duplicate (member, chore) rows producing duplicate UIDs.
        var seenUids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in export.Items)
        {
            var uid = BuildUid(item, export.Period, uidDomain);
            if (!seenUids.Add(uid))
            {
                continue;
            }

            var (firstOccurrence, rule) = BuildRecurrence(item.Frequency, export.Period);

            var calendarEvent = new CalendarEvent
            {
                Uid = uid,
                Summary = BuildSummary(item),
                // All-day: date-valued start with an EXCLUSIVE end one day later.
                Start = new CalDateTime(firstOccurrence),
                End = new CalDateTime(firstOccurrence.AddDays(1)),
                DtStamp = stamp,
            };

            if (rule is not null)
            {
                calendarEvent.RecurrenceRule = rule;
            }

            calendar.Events.Add(calendarEvent);
        }

        return new CalendarSerializer().SerializeToString(calendar) ?? string.Empty;
    }

    /// <summary>
    /// The <c>SUMMARY</c> line: <c>[MEMBER] [CADENCE] Chore name</c>. Both bracketed
    /// tokens are upper-cased for the at-a-glance Skylight tile, matching the issue's
    /// example (<c>[ALEX] [DAILY] Feed dog</c>).
    /// </summary>
    public static string BuildSummary(ChoreExportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var member = item.MemberName.ToUpperInvariant();
        var cadence = CadenceLabel(item.Frequency).ToUpperInvariant();
        return $"[{member}] [{cadence}] {item.ChoreName}";
    }

    /// <summary>
    /// Deterministic UID for a (member, chore, month) triple. Stable across re-sorts
    /// so Skylight updates rather than duplicates.
    /// </summary>
    public static string BuildUid(ChoreExportItem item, MonthPeriod period, string uidDomain = DefaultUidDomain)
    {
        ArgumentNullException.ThrowIfNull(item);
        return $"cw-{item.FamilyMemberId}-{item.ChoreId}-{period.Year:D4}{period.Month:D2}@{uidDomain}";
    }

    /// <summary>Human-facing cadence word for a frequency.</summary>
    public static string CadenceLabel(ChoreFrequency frequency) => frequency switch
    {
        ChoreFrequency.Daily => "Daily",
        ChoreFrequency.Weekly => "Weekly",
        ChoreFrequency.BiWeekly => "Bi-weekly",
        ChoreFrequency.Monthly => "Monthly",
        _ => frequency.ToString(),
    };

    /// <summary>
    /// Maps a cadence to its first occurrence date within <paramref name="period"/> and
    /// the recurrence rule (or <c>null</c> for a single occurrence).
    ///
    /// All four cadences are owner-confirmed and pinned by tests:
    ///   Daily    -> FREQ=DAILY;UNTIL=&lt;last day&gt;, starting the 1st.
    ///   Weekly   -> FREQ=WEEKLY;BYDAY=SA;UNTIL=&lt;last day&gt;, starting the first Saturday.
    ///   BiWeekly -> FREQ=WEEKLY;INTERVAL=2;BYDAY=SA, every other Saturday from the first.
    ///   Monthly  -> a single all-day event on the <b>first Saturday</b>, no recurrence.
    ///
    /// Every non-daily cadence lands on a Saturday by design: this household does its
    /// chores on the weekend, so a monthly chore anchored to the 1st would have fallen
    /// on an arbitrary weekday. Monthly therefore shares Weekly's start date and simply
    /// omits the recurrence rule.
    /// </summary>
    private static (DateOnly FirstOccurrence, RecurrencePattern? Rule) BuildRecurrence(
        ChoreFrequency frequency,
        MonthPeriod period)
    {
        var until = new CalDateTime(period.LastDay);
        var firstSaturday = FirstDayOfWeekOnOrAfter(period.FirstDay, DayOfWeek.Saturday);

        return frequency switch
        {
            ChoreFrequency.Daily => (
                period.FirstDay,
                new RecurrencePattern(FrequencyType.Daily) { Until = until }),

            ChoreFrequency.Weekly => (
                firstSaturday,
                new RecurrencePattern(FrequencyType.Weekly)
                {
                    Until = until,
                    ByDay = new List<WeekDay> { new(DayOfWeek.Saturday) },
                }),

            // Every other Saturday, starting the first Saturday of the month.
            ChoreFrequency.BiWeekly => (
                firstSaturday,
                new RecurrencePattern(FrequencyType.Weekly, 2)
                {
                    Until = until,
                    ByDay = new List<WeekDay> { new(DayOfWeek.Saturday) },
                }),

            // Single occurrence on the first Saturday, no RRULE. Deliberately NOT the
            // 1st, which can land on any weekday.
            ChoreFrequency.Monthly => (firstSaturday, null),

            _ => (period.FirstDay, null),
        };
    }

    /// <summary>The first date on or after <paramref name="start"/> that falls on <paramref name="dayOfWeek"/>.</summary>
    private static DateOnly FirstDayOfWeekOnOrAfter(DateOnly start, DayOfWeek dayOfWeek)
    {
        var delta = ((int)dayOfWeek - (int)start.DayOfWeek + 7) % 7;
        return start.AddDays(delta);
    }
}
