namespace HomeCompanion.Logics.MeteoSchweiz;

public class MeteoSchweizOptions
{
    public static string SectionName => "Logics:MeteoSchweiz";
    /// <summary>
    /// Swiss Postal Code (PLZ) for which the weather forecast should be retrieved.
    /// </summary>
    public int PLZ { get; set; } = 8606;

    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Cron trigger expressions for when the MeteoSchweizPollingJob should be executed. The default values are set to 02:00, 11:00, 17:00, and 20:00.
    /// </summary>
    public string[] PollingInstants { get; set; } =
    [
        "0 2 * * * ?",
        "0 11 * * * ?",
        "0 17 * * * ?",
        "0 20 * * * ?"
    ];
}