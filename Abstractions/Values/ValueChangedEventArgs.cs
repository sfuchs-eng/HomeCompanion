namespace HomeCompanion.Values;

public class ValueChangedEventArgs(IValue previousValue, IValue newValue, object? initiator = null) : ValueEventArgs(previousValue, newValue, initiator)
{
}
