using System.Text.Json.Serialization;
using HomeCompanion.Abstractions.Serialization;

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

    public CommunicationPermissions CommunicationPermissions { get; set; } = CommunicationPermissions.Full;
}

[JsonConverter(typeof(CommaSeparatedFlagsEnumJsonConverter<CommunicationPermissions>))]
[Flags]
public enum CommunicationPermissions
{
    /// <summary>
    /// HomeCompanion is allowed to read and write KNX group addresses.
    /// </summary>
    RxGroupAddressReadAnswers = 1 << 0,

    /// <summary>
    /// HomeCompanion is only allowed to read KNX group addresses, not write.
    /// </summary>
    RxGroupAddressReads = 1 << 1,

    RxGroupAdddressWrites = 1 << 2,

    TxGroupAddressReadAnswers = 1 << 3,

    TxGroupAddressWrites = 1 << 4,

    TxGroupAddressReads = 1 << 5,

    None = 0,
    Full = RxGroupAddressReadAnswers | RxGroupAddressReads | RxGroupAdddressWrites | TxGroupAddressReadAnswers | TxGroupAddressWrites | TxGroupAddressReads,
    RxOnly = RxGroupAddressReadAnswers | RxGroupAdddressWrites,
}
