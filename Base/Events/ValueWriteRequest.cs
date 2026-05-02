namespace HomeCompanion.Base.Events;

/// <summary>
/// Raised by <see cref="IValue.Write"/> when a logic writes a new value.
/// Connectivity providers subscribe to this event to receive write requests from logics and forward them to the connected bus.
/// </summary>
public class ValueWriteRequest : ValueEvent
{
    /// <summary>The value object that is the target of the write request.</summary>
    public required IValue Source { get; init; }

    /// <summary>The new value, untyped. Use <see cref="ValueWriteRequest{T}.TypedValue"/> for the typed accessor.</summary>
    public object? NewValue { get; init; }
}

/// <summary>Typed variant of <see cref="ValueWriteRequest"/>.</summary>
/// <typeparam name="T">The value type.</typeparam>
public class ValueWriteRequest<T> : ValueWriteRequest
{
    /// <summary>Typed accessor for <see cref="ValueWriteRequest.NewValue"/>.</summary>
    public T TypedValue => (T)NewValue!;
}
