namespace HomeCompanion.Base.Values;

public class ValueChangedEventArgs(IValue previousValue, IValue newValue) : EventArgs
{
    public IValue PreviousValue { get; } = previousValue;
    public IValue NewValue { get; } = newValue;
}
