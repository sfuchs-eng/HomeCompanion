namespace HomeCompanion.Abstractions;

public interface IValue
{
    public Type ValueType { get; }
    public ValueStatus Status { get; }
}

public interface IValue<T> : IValue
{
    public T Value { get; }
}
