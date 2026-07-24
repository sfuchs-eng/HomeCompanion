namespace HomeCompanion.Events;

/// <summary>
/// Event contract for calendar-triggered events emitted by the calendar scheduler.
/// </summary>
public interface ICalendarEvent : IEvent
{
    /// <summary>Gets or sets the source calendar entry id.</summary>
    Guid CalendarEntryId { get; set; }

    /// <summary>Gets or sets the source calendar entry title.</summary>
    string CalendarEntryTitle { get; set; }

    /// <summary>Gets or sets whether this is a start or end firing.</summary>
    CalendarEventPhase Phase { get; set; }

    /// <summary>Gets or sets optional metadata payload as JSON object text.</summary>
    string? MetadataJson { get; set; }
}

/// <summary>
/// Identifies the trigger phase for a calendar event publication.
/// </summary>
public enum CalendarEventPhase
{
    Start = 0,
    End = 1,
}
