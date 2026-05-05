namespace HomeCompanion.Values;

public class ValueChangedEventArgs(IValue previousValue, IValue newValue, object? initiator = null) : EventArgs
{
    public IValue PreviousValue { get; } = previousValue;
    public IValue NewValue { get; } = newValue;
    public object? Initiator { get; } = initiator;
}
