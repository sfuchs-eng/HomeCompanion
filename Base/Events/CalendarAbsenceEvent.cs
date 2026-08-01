namespace HomeCompanion.Events;

[CalendarEventType("Absence", Description = "Calendar configured absence event with optional metadata.", Category = "Automation")]
public sealed class CalendarAbsenceEvent : HomeCompanionEvent, ICalendarEvent
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