namespace HomeCompanion.Integrations.Knx;

/// <summary>
/// HomeCompanion integration-layer options for KNX.
/// </summary>
/// <remarks>
/// Bound from the <c>Knx</c> configuration section.
/// These options govern HomeCompanion-specific integration behavior and are separate from
/// transport connectivity settings (<see cref="SRF.Network.Knx.KnxConnectionOptions"/>) and
/// file/config-generation settings (<c>SRF.Knx.Config.KnxSystemConfigOptions</c>).
/// </remarks>
public sealed class KnxIntegrationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Knx";

    /// <summary>
    /// Whether the KNX integration is enabled.
    /// When <see langword="false"/> the <see cref="KnxConnectivityProvider"/> is inactive even
    /// if connections are configured.
    /// </summary>
    public bool Enable { get; set; } = true;

    /// <summary>
    /// If <see langword="true"/>, HomeCompanion sends a KNX GroupValueRead for every registered
    /// group address at startup so that bus devices respond with their current state, completing
    /// initial value population.
    /// </summary>
    public bool ReadGroupAddressesOnStartup { get; set; } = true;
}
