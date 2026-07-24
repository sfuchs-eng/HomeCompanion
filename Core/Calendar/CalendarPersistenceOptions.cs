namespace HomeCompanion.Core.Calendar;

/// <summary>
/// Options for EF Core calendar persistence.
/// </summary>
public sealed class CalendarPersistenceOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "HomeCompanion:CalendarPersistence";

    /// <summary>
    /// SQLite database path, absolute or relative to the application base directory.
    /// </summary>
    public string DbPath { get; set; } = "state/calendar/calendar.db";
}
