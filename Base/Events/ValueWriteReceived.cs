namespace HomeCompanion.Events;

/// <summary>
/// Published by a connectivity provider when an inbound bus write telegram arrives for a registered value.
/// <see cref="ValueBase{T}"/> subscribes to this event to update its stored value.
/// </summary>
public class ValueWriteReceived : ValueEvent
{
    /// <summary>The value object that should be updated, or <see langword="null"/> if no <see cref="IValue"/> is registered for the bus address.</summary>
    public IValue? Target { get; init; }

    /// <summary>The decoded value received from the bus, or <see langword="null"/> if decoding failed.</summary>
    public object? NewValue { get; init; }
}
