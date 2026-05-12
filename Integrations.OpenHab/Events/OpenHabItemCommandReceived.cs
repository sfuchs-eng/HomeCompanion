using HomeCompanion.Events;

namespace HomeCompanion.Integrations.OpenHab.Events;

/// <summary>
/// Published by the OpenHab connectivity provider when an <see cref="SRF.Network.OpenHab.EventBus.EventType.ItemCommandEvent"/> is received from the OpenHab event bus.
/// </summary>
public class OpenHabItemCommandReceived : ValueWriteReceived
{
    /// <summary>The OpenHab item name that received the command.</summary>
    public required string ItemName { get; init; }

    /// <summary>The raw command value from OpenHab (string).</summary>
    public required string RawCommand { get; init; }
}
