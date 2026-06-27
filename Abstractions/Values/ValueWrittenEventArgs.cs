namespace HomeCompanion.Values;

public class ValueWrittenEventArgs(IValue previousValue, IValue newValue, DateTimeOffset timestamp, object? initiator = null) : ValueEventArgs(previousValue, newValue, initiator)
{
    public DateTimeOffset Timestamp { get; } = timestamp;
}
