namespace HomeCompanion.Base.Values;

/// <summary>
/// The value type agnostic part for <see cref="ValueBase{T}"/> 
/// </summary>
public class ValueBase : IValue
{
    public Type ValueType => GetType();
    public ValueStatus Status { get; protected set; }
    public string? Name { get; set; }
    public string? Label { get; set; }

    public event EventHandler<ValueWrittenEventArgs>? Written;
    public event EventHandler<ValueChangedEventArgs>? Changed;

    /// <summary>
    /// See <see cref="IValueBusMapping"/> for details on the purpose of this property.
    /// Use <see cref="ValueBusMapping{TBus, TAddress}"/> for a concrete implementation of <see cref="IValueBusMapping"/> for a specific bus type (e.g. KNX).
    /// </summary>
    public Dictionary<object, IValueBusMapping> BusMappings { get; } = [];

    public bool TryGetBusMapping<TBusMapping>(object busIdentifier, out TBusMapping? mapping) where TBusMapping : IValueBusMapping
    {
        if (BusMappings.TryGetValue(busIdentifier, out var value) && value is TBusMapping typedValue)
        {
            mapping = typedValue;
            return true;
        }
        mapping = default;
        return false;
    }

    public void AddBusMapping(object busIdentifier, IValueBusMapping mapping)
    {
        BusMappings[busIdentifier] = mapping;
    }
}

public class ValueBase<T> : ValueBase, IValue<T>
{
    public T Value { get; set; } = default!;

    /// <inheritdoc/>
    public virtual void Write(T value)
    {
        throw new NotImplementedException("Update value, send a write request event to the event bus, call event handlers");
    }
}