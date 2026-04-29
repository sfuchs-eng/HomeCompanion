namespace HomeCompanion.Base.Events;

/// <summary>
/// Published when a value's stored data actually changes (old value differs from new value).
/// Bus-agnostic: any connectivity provider or logic can observe this event regardless of the transport that caused the change.
/// </summary>
public class ValueChanged : ValueEvent { }

/// <summary>
/// Typed variant of <see cref="ValueChanged"/>, published by <c>KnxValue&lt;T&gt;</c> (and similar) whenever the
/// stored value transitions from <paramref name="OldValue"/> to <paramref name="NewValue"/>.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
public class ValueChanged<T> : ValueChanged
{
    /// <summary>The value before the change.</summary>
    public required T OldValue { get; init; }

    /// <summary>The value after the change.</summary>
    public required T NewValue { get; init; }

    /// <summary>The <see cref="IValue{T}"/> instance whose value changed.</summary>
    public required IValue<T> Source { get; init; }
}
