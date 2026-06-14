namespace HomeCompanion.Values;

public class ValueWrittenEventArgs(IValue previousValue, IValue newValue, object? initiator = null) : ValueEventArgs(previousValue, newValue, initiator)
{
}
