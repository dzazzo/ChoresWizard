using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Zazzo.ChoresWizard2000.Models;
using Zazzo.ChoresWizard2000.Models.Export;

namespace Zazzo.ChoresWizard2000.Services.Export;

/// <summary>
/// Renders a printable, fridge-friendly PDF chore chart for a month (issue #9).
///
/// Owner requirements honoured here:
/// <list type="bullet">
///   <item>The header states the <b>full</b> month span (1st &#8594; last day),
///   derived from <see cref="MonthPeriod"/>, never from the day the sort ran.</item>
///   <item>Daily chores and Weekly (Saturday) chores are grouped under clearly
///   labelled, visually distinct sections per family member.</item>
/// </list>
/// </summary>
public static class ChorePdfBuilder
{
    // QuestPDF's Community licence is free for organisations under $1M USD annual
    // revenue. This household app qualifies; acknowledging it is a real licence term.
    // The canonical acknowledgement is at startup (Program.cs); this static
    // constructor mirrors it so PDF generation also works in test hosts that never
    // run Program.cs. Setting it is idempotent.
    static ChorePdfBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>
    /// Title printed at the top of the chart.
    ///
    /// Deliberately free of emoji. QuestPDF embeds its default Lato face and the Linux
    /// container carries no emoji font, so any pictograph renders as a "tofu" box on the
    /// printed page — a broom emoji here shipped exactly that. Keep this ASCII; see
    /// <c>ChorePdfGlyphCoverageTests</c>, which fails the build if it drifts.
    /// </summary>
    public const string DocumentTitle = "Zazzo Family Chore Chart";

    // Order cadences deliberately: Daily first, then the Saturday cadences, then Monthly.
    private static readonly ChoreFrequency[] CadenceOrder =
    [
        ChoreFrequency.Daily,
        ChoreFrequency.Weekly,
        ChoreFrequency.BiWeekly,
        ChoreFrequency.Monthly,
    ];

    public static byte[] Build(MonthlyChoreExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        var spanText = BuildSpanText(export.Period);

        var members = export.Items
            .GroupBy(i => (i.FamilyMemberId, i.MemberName))
            .OrderBy(g => g.Key.MemberName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Grey.Darken4));

                page.Header().Column(header =>
                {
                    header.Item().Text(DocumentTitle)
                        .FontSize(24).Bold().FontColor(Colors.Purple.Darken3);
                    header.Item().Text(spanText)
                        .FontSize(14).SemiBold().FontColor(Colors.Grey.Darken2);
                    header.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Purple.Medium);
                });

                page.Content().PaddingVertical(10).Column(content =>
                {
                    content.Spacing(14);

                    if (members.Count == 0)
                    {
                        content.Item().PaddingTop(20).Text("No chores assigned for this month yet.")
                            .Italic().FontColor(Colors.Grey.Medium);
                        return;
                    }

                    foreach (var member in members)
                    {
                        content.Item().Element(c => ComposeMember(c, member.Key.MemberName, member));
                    }
                });

                page.Footer().Column(footer =>
                {
                    footer.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                    footer.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text(
                            $"Covers {spanText}. Daily = every day; Weekly = every Saturday.")
                            .FontSize(9).FontColor(Colors.Grey.Medium);
                        row.ConstantItem(90).AlignRight().Text(x =>
                        {
                            x.Span("Page ").FontSize(9).FontColor(Colors.Grey.Medium);
                            x.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Medium);
                            x.Span(" / ").FontSize(9).FontColor(Colors.Grey.Medium);
                            x.TotalPages().FontSize(9).FontColor(Colors.Grey.Medium);
                        });
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeMember(IContainer container, string memberName, IEnumerable<ChoreExportItem> items)
    {
        var byCadence = items
            .GroupBy(i => i.Frequency)
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.ChoreName, StringComparer.OrdinalIgnoreCase).ToList());

        container
            .Border(1).BorderColor(Colors.Purple.Lighten2)
            .Background(Colors.Purple.Lighten5)
            .Padding(12)
            .Column(col =>
            {
                col.Spacing(8);
                col.Item().Text(memberName).FontSize(18).Bold().FontColor(Colors.Purple.Darken2);

                foreach (var cadence in CadenceOrder)
                {
                    if (!byCadence.TryGetValue(cadence, out var choreList) || choreList.Count == 0)
                    {
                        continue;
                    }

                    col.Item().Element(c => ComposeCadenceGroup(c, cadence, choreList));
                }
            });
    }

    private static void ComposeCadenceGroup(IContainer container, ChoreFrequency cadence, List<ChoreExportItem> chores)
    {
        container.Column(group =>
        {
            group.Spacing(3);
            group.Item().Text(PdfCadenceLabel(cadence))
                .FontSize(12).Bold().FontColor(Colors.Grey.Darken3);

            foreach (var chore in chores)
            {
                group.Item().Row(row =>
                {
                    // Drawn as a vector square rather than the "☐" character. That glyph is
                    // absent from the embedded font, so it printed as a tofu box on every
                    // line. A real rectangle needs no font coverage at all and gives a
                    // crisper tick box to check off on the fridge.
                    row.ConstantItem(18).AlignMiddle().Element(box => box
                        .Width(11).Height(11)
                        .Border(1).BorderColor(Colors.Grey.Darken1)
                        .Background(Colors.White));
                    row.RelativeItem().Text(chore.ChoreName).FontSize(11);
                });
            }
        });
    }

    // Explicitly names Saturday for the weekly cadences so the printed chart is
    // unambiguous, as the owner asked. Public so glyph-coverage tests can assert these
    // stay printable in the embedded font.
    public static string PdfCadenceLabel(ChoreFrequency cadence) => cadence switch
    {
        ChoreFrequency.Daily => "Daily",
        ChoreFrequency.Weekly => "Weekly (Saturdays)",
        ChoreFrequency.BiWeekly => "Bi-weekly (Saturdays)",
        ChoreFrequency.Monthly => "Monthly",
        _ => cadence.ToString(),
    };

    private static string BuildSpanText(MonthPeriod period)
    {
        var first = period.FirstDay;
        var last = period.LastDay;
        // e.g. "August 1 – 31, 2026"
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:MMMM} {1} – {2}, {3}",
            first.ToDateTime(TimeOnly.MinValue),
            first.Day,
            last.Day,
            period.Year);
    }
}
