namespace Zazzo.ChoresWizard2000.Tests;

/// <summary>
/// A trivial <see cref="TimeProvider"/> whose "now" is a fixed, settable instant.
/// Lets tests drive month determination from chosen UTC instants with zero wall-clock
/// dependence — no reliance on the machine's real time or time zone.
/// </summary>
public sealed class FixedTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Set(DateTimeOffset utcNow) => _utcNow = utcNow;
}
