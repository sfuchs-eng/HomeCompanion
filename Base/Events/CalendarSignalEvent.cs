namespace HomeCompanion.Events;

/// <summary>
/// Generic built-in calendar signal event that can be scheduled from the calendar UI.
/// </summary>
[CalendarEventType("Calendar Signal", Description = "Generic calendar signal event with optional metadata.", Category = "Calendar")]
public sealed class CalendarSignalEvent : HomeCompanionEvent, ICalendarEvent
{
    /// <inheritdoc/>
    public Guid CalendarEntryId { get; set; }

    /// <inheritdoc/>
    public string CalendarEntryTitle { get; set; } = string.Empty;

    /// <inheritdoc/>
    public CalendarEventPhase Phase { get; set; }

    /// <inheritdoc/>
    public string? MetadataJson { get; set; }
}
