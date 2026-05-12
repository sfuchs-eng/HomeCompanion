using HomeCompanion.Events;

namespace HomeCompanion.Integrations.OpenHab.Events;

/// <summary>
/// Published by the OpenHab connectivity provider when an <see cref="SRF.Network.OpenHab.EventBus.Events.ItemStateChangedEvent"/> is received from the OpenHab event bus.
/// </summary>
public class OpenHabItemStateChanged : ValueUpdateReceived
{
    /// <summary>The OpenHab item name that changed state.</summary>
    public required string ItemName { get; init; }

    /// <summary>The raw state value from OpenHab (string).</summary>
    public required string RawState { get; init; }

    /// <summary>The previous state value from OpenHab (string).</summary>
    public required string OldRawState { get; init; }
}
