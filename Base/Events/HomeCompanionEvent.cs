namespace HomeCompanion.Events;

public class HomeCompanionEvent : IEvent
{
    /// <inheritdoc/>
    public DateTimeOffset Timestamp { get; init; }
}
