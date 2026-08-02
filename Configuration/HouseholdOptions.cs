namespace Zazzo.ChoresWizard2000.Configuration;

/// <summary>
/// Household-wide settings, most importantly the local time zone used to decide
/// "what month is it right now". Bound from the "Household" configuration section.
/// </summary>
public sealed class HouseholdOptions
{
    /// <summary>Configuration section name (e.g. appsettings.json "Household").</summary>
    public const string SectionName = "Household";

    /// <summary>
    /// Default IANA time zone id for the household. The app runs on Linux App Service
    /// but is developed on macOS; .NET 10 resolves IANA ids on every platform via
    /// <see cref="System.TimeZoneInfo.FindSystemTimeZoneById(string)"/>.
    /// </summary>
    public const string DefaultTimeZone = "America/Los_Angeles";

    /// <summary>
    /// IANA time zone id used for all month-boundary reasoning. Defaults to
    /// <see cref="DefaultTimeZone"/> when unset.
    /// </summary>
    public string TimeZone { get; set; } = DefaultTimeZone;
}
