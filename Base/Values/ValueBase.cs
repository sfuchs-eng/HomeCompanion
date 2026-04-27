namespace HomeCompanion.Base.Values;

public class ValueBase : IValue
{
    public Type ValueType => GetType();
    public ValueStatus Status { get; protected set; }
}

public class ValueBase<T> : ValueBase, IValue<T>
{
    public T Value { get; set; } = default!;
}