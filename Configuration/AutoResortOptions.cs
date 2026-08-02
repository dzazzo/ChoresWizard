namespace Zazzo.ChoresWizard2000.Configuration;

/// <summary>
/// Settings for the automatic monthly re-sort (issue #5). Bound from the "AutoResort"
/// configuration section. The schedule is in-process (a <see cref="System.Threading.Tasks.Task"/>-based
/// <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>), so there is no external
/// trigger, endpoint, or secret to manage — the operator only decides whether it is on and how
/// often it wakes to check.
/// </summary>
public sealed class AutoResortOptions
{
    /// <summary>Configuration section name (e.g. appsettings.json "AutoResort").</summary>
    public const string SectionName = "AutoResort";

    private const int DefaultCheckIntervalHours = 6;

    /// <summary>
    /// Whether the in-process auto-resort scheduler runs. Defaults to <c>true</c>. Set
    /// <c>AutoResort:Enabled=false</c> to disable the schedule without a code change.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often (hours) the scheduler wakes to check whether it is the last local day of the
    /// month. Anything comfortably shorter than a day works: the check is cheap and idempotent,
    /// so several checks across the last day just confirm the month is already generated.
    /// Defaults to <c>6</c>. Non-positive values fall back to the default.
    /// </summary>
    public int CheckIntervalHours { get; set; } = DefaultCheckIntervalHours;

    /// <summary><see cref="CheckIntervalHours"/> as a <see cref="TimeSpan"/>, guarded against non-positive values.</summary>
    public TimeSpan CheckInterval =>
        TimeSpan.FromHours(CheckIntervalHours > 0 ? CheckIntervalHours : DefaultCheckIntervalHours);

    /// <summary>
    /// Delay before the first check after startup, giving the background database migration
    /// (issue #2) a head start so the first check does not race an unmigrated schema.
    /// </summary>
    public TimeSpan StartupDelay { get; } = TimeSpan.FromSeconds(30);
}
