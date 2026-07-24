namespace HomeCompanion.Events;

/// <summary>
/// Marks an <see cref="ICalendarEvent"/> implementation as selectable in the calendar UI.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CalendarEventTypeAttribute(string displayName) : Attribute
{
    /// <summary>Gets the user-facing display name.</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>Gets or sets an optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets or sets an optional category.</summary>
    public string? Category { get; init; }
}
