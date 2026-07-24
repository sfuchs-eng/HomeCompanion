namespace HomeCompanion.Calendar;

/// <summary>
/// Provides the list of event types available for calendar scheduling.
/// </summary>
public interface ICalendarEventTypeRegistry
{
    /// <summary>Gets all available event type descriptors.</summary>
    IReadOnlyList<CalendarEventTypeDescriptor> ListEventTypes();

    /// <summary>Resolves a calendar event type by assembly-qualified type name.</summary>
    Type? ResolveEventType(string typeName);
}
