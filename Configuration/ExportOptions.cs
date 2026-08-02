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
    /// Name of the output-cache policy applied to the anonymous ICS feed.
    /// </summary>
    public const string FeedCachePolicyName = "SkylightFeed";

    /// <summary>
    /// Default feed cache lifetime, used when configuration supplies a non-positive value.
    /// </summary>
    public const int DefaultFeedCacheSeconds = 3600;

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

    /// <summary>
    /// How long (seconds) a generated ICS feed response is served from the output cache
    /// before the database is queried again.
    ///
    /// <para>This exists for cost, not speed. The Azure SQL database is a serverless
    /// tier that auto-pauses after 60 minutes idle; Skylight polls the subscribed feed on
    /// its own schedule, so an uncached feed would query the database often enough to keep
    /// the database permanently awake and billing. Assignments change at most once a
    /// month, so caching costs nothing in freshness.</para>
    ///
    /// <para>Values ≤ 0 fall back to <see cref="DefaultFeedCacheSeconds"/>, so a
    /// misconfiguration cannot accidentally disable caching and start the meter running.</para>
    /// </summary>
    public int FeedCacheSeconds { get; set; } = DefaultFeedCacheSeconds;

    /// <summary>
    /// The effective cache lifetime, clamping non-positive configured values to
    /// <see cref="DefaultFeedCacheSeconds"/>.
    /// </summary>
    public TimeSpan ResolvedFeedCacheDuration =>
        TimeSpan.FromSeconds(FeedCacheSeconds > 0 ? FeedCacheSeconds : DefaultFeedCacheSeconds);
}
