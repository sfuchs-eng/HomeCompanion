using HomeCompanion.Abstractions;
using SRF.Knx.Core;
using SRF.Network.Knx.Messages;

namespace HomeCompanion.Knx.Events;

/// <summary>
/// Published by the KNX connectivity provider when a <c>GroupValueResponse</c> telegram is received from the bus.
/// This is typically the answer to a prior <c>GroupValueRead</c> request; it carries the current value of the
/// addressed group object and is used to initialize <see cref="IValue"/> instances that were not yet current.
/// </summary>
public class KnxGroupResponseReceived : IEvent
{
    /// <summary>The KNX group address the response was sent to.</summary>
    public required GroupAddress DestinationAddress { get; init; }

    /// <summary>The physical address of the KNX device that answered the read.</summary>
    public required IndividualAddress SourceAddress { get; init; }

    /// <summary>Raw KNX bus payload.</summary>
    public required GroupValue RawValue { get; init; }

    /// <summary>
    /// Decoded typed value, or <see langword="null"/> if the DPT could not be resolved.
    /// </summary>
    public object? DecodedValue { get; init; }

    /// <summary>Timestamp at which the telegram was received from the bus.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }
}
