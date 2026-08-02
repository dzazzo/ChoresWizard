using System.Globalization;
using System.Text;
using Zazzo.ChoresWizard2000.Models.Export;

namespace Zazzo.ChoresWizard2000.Services.Export;

/// <summary>
/// Renders a month's chore assignments as RFC 4180 CSV. Skylight has no documented
/// CSV importer, so this is a low-effort generic fallback for spreadsheets and a
/// possible Sidekick attempt (issue #9). Pure and DB-free.
///
/// Every row repeats the full month span (1st and last day) so the file is
/// self-describing regardless of when the sort ran.
/// </summary>
public static class ChoreCsvBuilder
{
    private static readonly string[] Header =
        ["Member", "Chore", "Cadence", "Category", "MonthStart", "MonthEnd"];

    public static string Build(MonthlyChoreExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        var monthStart = export.Period.FirstDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var monthEnd = export.Period.LastDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append(string.Join(',', Header.Select(Escape))).Append("\r\n");

        foreach (var item in export.Items)
        {
            var row = new[]
            {
                item.MemberName,
                item.ChoreName,
                SkylightIcsBuilder.CadenceLabel(item.Frequency),
                item.Category ?? string.Empty,
                monthStart,
                monthEnd,
            };
            sb.Append(string.Join(',', row.Select(Escape))).Append("\r\n");
        }

        return sb.ToString();
    }

    // RFC 4180: quote fields containing comma, quote, CR or LF; double embedded quotes.
    private static string Escape(string field)
    {
        field ??= string.Empty;
        var needsQuoting = field.Contains(',') || field.Contains('"')
            || field.Contains('\n') || field.Contains('\r');
        if (!needsQuoting)
        {
            return field;
        }

        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
