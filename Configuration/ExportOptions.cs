namespace Zazzo.ChoresWizard2000.Configuration;

/// <summary>
/// Settings for the Skylight chore export (issue #9). Bound from the "Export"
/// configuration section.
/// </summary>
public sealed class ExportOptions
{
    /// <summary>Configuration section name (e.g. appsettings.json "Export").</summary>
    public const string SectionName = "Export";

    /// <summary>
    /// Unguessable token that gates the anonymous ICS feed route
    /// (<c>/feed/{token}/chores.ics</c>). Skylight cannot authenticate, so this
    /// token in the URL is the ONLY protection on the feed.
    ///
    /// It is intentionally left empty in committed config: set it out of band via
    /// user-secrets (dev) or an App Service application setting <c>Export__FeedToken</c>
    /// (prod). While empty, the feed is disabled and returns 404. Never hardcode,
    /// log, or commit a real value. Rotate by simply changing this setting.
    /// </summary>
    public string? FeedToken { get; set; }
}
