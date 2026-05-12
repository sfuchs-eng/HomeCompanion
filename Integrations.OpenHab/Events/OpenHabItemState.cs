using HomeCompanion.Events;

namespace HomeCompanion.Integrations.OpenHab.Events;

/// <summary>
/// Published by the OpenHab connectivity provider when an <see cref="SRF.Network.OpenHab.EventBus.EventType.ItemStateEvent"/> is received from the OpenHab event bus.
/// </summary>
public class OpenHabItemState : ValueUpdateReceived
{
    /// <summary>The OpenHab item name whose state was reported.</summary>
    public required string ItemName { get; init; }

    /// <summary>The raw state value from OpenHab (string).</summary>
    public required string RawState { get; init; }
}
