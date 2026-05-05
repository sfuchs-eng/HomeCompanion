namespace HomeCompanion.Events;

/// <summary>
/// Published by a connectivity provider when a bus read request is received for a registered value.
/// </summary>
public class ValueReadReceived : ValueEvent
{
    /// <summary>The value object that was requested, or <see langword="null"/> if no <see cref="IValue"/> is registered for the bus address.</summary>
    public IValue? Target { get; init; }
}
