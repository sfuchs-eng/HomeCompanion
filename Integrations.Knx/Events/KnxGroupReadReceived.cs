using HomeCompanion.Abstractions;
using SRF.Knx.Core;
using SRF.Network.Knx.Messages;

namespace HomeCompanion.Integrations.Knx.Events;

/// <summary>
/// Published by the KNX connectivity provider when a <c>GroupValueRead</c> telegram is received from the bus.
/// A logic or provider that owns the value for this group address should respond with a
/// <c>GroupValueResponse</c> telegram.
/// </summary>
public class KnxGroupReadReceived : IEvent
{
    /// <summary>The KNX group address that was read.</summary>
    public required GroupAddress DestinationAddress { get; init; }

    /// <summary>The physical address of the KNX device that issued the read request.</summary>
    public required IndividualAddress SourceAddress { get; init; }

    /// <summary>Timestamp at which the telegram was received from the bus.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }
}
