namespace HomeCompanion.Base.Events;

/// <summary>
/// Published by <see cref="ValueBase{T}.Write"/> when a logic writes a new value.
/// Connectivity providers subscribe to this event to forward the write to the connected bus.
/// </summary>
public class ValueWritten : ValueEvent
{
    /// <summary>The value object that was written to.</summary>
    public required IValue Source { get; init; }

    /// <summary>The new value, untyped. Use <see cref="ValueWritten{T}.TypedValue"/> for the typed accessor.</summary>
    public object? Value { get; init; }
}

/// <summary>Typed variant of <see cref="ValueWritten"/>.</summary>
/// <typeparam name="T">The value type.</typeparam>
public class ValueWritten<T> : ValueWritten
{
    /// <summary>Typed accessor for <see cref="ValueWritten.Value"/>.</summary>
    public T TypedValue => (T)Value!;
}
