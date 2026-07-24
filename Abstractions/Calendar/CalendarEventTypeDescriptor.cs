namespace HomeCompanion.Calendar;

/// <summary>
/// Describes an attributed calendar event type that can be selected by users in the calendar UI.
/// </summary>
public sealed class CalendarEventTypeDescriptor
{
    /// <summary>Gets or sets the assembly-qualified event type name.</summary>
    public required string TypeName { get; init; }

    /// <summary>Gets or sets the simple CLR type name.</summary>
    public required string TypeShortName { get; init; }

    /// <summary>Gets or sets a user-facing display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gets or sets an optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets or sets an optional category.</summary>
    public string? Category { get; init; }
}
