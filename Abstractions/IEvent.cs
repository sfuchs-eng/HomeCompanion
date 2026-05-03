namespace HomeCompanion.Abstractions;

/// <summary>
/// Represents a generic event in the HomeCompanion system managed via the event bus.
/// Specific event types should inherit from <see cref="HomeCompanion.Base.Events.HomeCompanionEvent"/>
/// instead of implementing this interface directly.
/// </summary>
public interface IEvent
{
    /// <summary>Gets the UTC timestamp at which this event was created or received.</summary>
    DateTimeOffset Timestamp { get; init; }
}
