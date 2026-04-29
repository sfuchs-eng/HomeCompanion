namespace HomeCompanion.Base.Values;

public class ValueWrittenEventArgs(IValue previousValue, IValue newValue) : EventArgs
{
    public IValue PreviousValue { get; } = previousValue;
    public IValue NewValue { get; } = newValue;
}
