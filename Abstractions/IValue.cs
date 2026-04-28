namespace HomeCompanion.Abstractions;

/// <summary>
/// A datapoint value in the HomeCompanion system. Values are managed by the IValuesManager and can be written to by logics or connectivity providers, and read by logics or connectivity providers.
/// Values can be initialized with a default value or updated based on bus telegrams or API calls. See also <see cref="ValueInitialization"/> and <see cref="ValueWritten"/> events as well as <see cref="IValuesContainer"/> for classes
/// </summary>
public interface IValue
{
    public Type ValueType { get; }
    public ValueStatus Status { get; }
    public string? Name { get; }
    public string? Label { get; }
}

public interface IValue<T> : IValue
{
    public T Value { get; }
}
