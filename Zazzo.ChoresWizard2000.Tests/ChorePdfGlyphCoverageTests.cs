using System.Globalization;
using Xunit;
using Zazzo.ChoresWizard2000.Models;
using Zazzo.ChoresWizard2000.Services.Export;

namespace Zazzo.ChoresWizard2000.Tests;

/// <summary>
/// QuestPDF embeds its default Lato face, and the Linux container the app runs in ships no
/// emoji font. Any character outside that face's coverage is drawn as a "tofu" box on the
/// printed page — silently, because generation still succeeds and the bytes still look like
/// a valid PDF.
///
/// That is exactly how a broom emoji in the title and a "☐" on every chore line reached
/// production looking broken. These tests pin the text the chart emits to characters the
/// embedded font can actually draw, so a decorative pictograph cannot sneak back in.
///
/// The checkbox is no longer text at all — it is a vector rectangle, which needs no font
/// coverage whatsoever.
/// </summary>
public class ChorePdfGlyphCoverageTests
{
    /// <summary>
    /// Characters above ASCII that we have visually confirmed render in the embedded font.
    /// Anything else must be justified and added here deliberately.
    /// </summary>
    private static readonly HashSet<char> AllowedNonAscii = ['\u2013']; // en dash

    private static void AssertPrintable(string text, string because)
    {
        foreach (var ch in text)
        {
            if (ch <= '\u007F' || AllowedNonAscii.Contains(ch))
            {
                continue;
            }

            Assert.Fail(
                $"{because} contains U+{(int)ch:X4} ('{ch}'), which the embedded PDF font " +
                "cannot draw — it will print as an empty box. Use plain text, or draw the " +
                "shape as vector geometry instead.");
        }
    }

    [Fact]
    public void DocumentTitle_ContainsNoUndrawableGlyphs()
    {
        AssertPrintable(ChorePdfBuilder.DocumentTitle, "The PDF document title");
    }

    [Theory]
    [InlineData(ChoreFrequency.Daily)]
    [InlineData(ChoreFrequency.Weekly)]
    [InlineData(ChoreFrequency.BiWeekly)]
    [InlineData(ChoreFrequency.Monthly)]
    public void CadenceLabels_ContainNoUndrawableGlyphs(ChoreFrequency cadence)
    {
        AssertPrintable(ChorePdfBuilder.PdfCadenceLabel(cadence), $"The {cadence} cadence label");
    }

    /// <summary>
    /// The header emoji was the specific regression reported from production. Naming it
    /// keeps the intent greppable rather than relying on the range check alone.
    /// </summary>
    [Fact]
    public void DocumentTitle_HasNoEmoji()
    {
        Assert.DoesNotContain("🧹", ChorePdfBuilder.DocumentTitle, StringComparison.Ordinal);
        Assert.Equal("Zazzo Family Chore Chart", ChorePdfBuilder.DocumentTitle);
    }
}
