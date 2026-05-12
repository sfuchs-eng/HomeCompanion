namespace HomeCompanion.Events;

/// <summary>
/// Published by a connectivity provider when an inbound bus state update arrives for a registered value.
/// This represents an observed value/state from the bus, independent of whether it originated from a command.
/// </summary>
public class ValueUpdateReceived : ValueEvent
{
    /// <summary>The value object that should be updated, or <see langword="null"/> if no <see cref="IValue"/> is registered for the bus address.</summary>
    public IValue? Target { get; init; }

    /// <summary>The decoded value received from the bus update, or <see langword="null"/> if decoding failed.</summary>
    public object? Value { get; init; }
}
