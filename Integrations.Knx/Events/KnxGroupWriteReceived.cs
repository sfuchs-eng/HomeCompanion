using HomeCompanion.Events;
using SRF.Knx.Core;
using SRF.Network.Knx.Messages;

namespace HomeCompanion.Integrations.Knx.Events;

/// <summary>
/// Published by the KNX connectivity provider when a <c>GroupValueWrite</c> telegram is received from the bus.
/// </summary>
public class KnxGroupWriteReceived : ValueUpdateReceived
{
    /// <summary>The KNX group address the telegram was sent to.</summary>
    public required GroupAddress DestinationAddress { get; init; }

    /// <summary>The physical address of the KNX device that sent the telegram.</summary>
    public required IndividualAddress SourceAddress { get; init; }

    /// <summary>Raw KNX bus payload.</summary>
    public required GroupValue RawValue { get; init; }

    /// <summary>
    /// Decoded typed value, or <see langword="null"/> if the DPT could not be resolved.
    /// </summary>
    public object? DecodedValue { get; init; }

}
