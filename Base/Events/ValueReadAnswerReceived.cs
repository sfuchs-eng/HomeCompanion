namespace HomeCompanion.Base.Events;

/// <summary>
/// Published by a connectivity provider when a bus read response is received for a registered value.
/// </summary>
public class ValueReadAnswerReceived : ValueEvent
{
    /// <summary>The value object that received the response, or <see langword="null"/> if no <see cref="IValue"/> is registered for the bus address.</summary>
    public IValue? Target { get; init; }

    /// <summary>The decoded value from the response, or <see langword="null"/> if decoding failed.</summary>
    public object? Value { get; init; }
}
