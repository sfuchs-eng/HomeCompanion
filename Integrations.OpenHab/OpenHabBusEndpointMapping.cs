using HomeCompanion.Values;

namespace HomeCompanion.Integrations.OpenHab;

/// <summary>
/// Maps an <see cref="IValue"/> to an OpenHAB item name.
/// Conceptually, it would make more sense to map to channels instead of items.
/// But for simplicity reasons mapping directly to items cuts on efforts to add new items, e.g. related to <see cref="Logics.ILogic"/> parameterization needs.
/// </summary>
/// <remarks>
/// Add this mapping to <see cref="IValue.BusMappings"/> under the key <see cref="BusId"/> to register
/// a value as OpenHAB-backed for initial state retrieval from the OpenHAB REST API.
/// </remarks>
public sealed class OpenHabBusEndpointMapping : ValueBusMapping<string, string>
{
    /// <summary>
    /// The bus identifier used as the dictionary key in <see cref="IValue.BusMappings"/> for OpenHAB mappings.
    /// </summary>
    public static readonly string BusId = "openhab";

    /// <summary>The OpenHAB item name this value is mapped to.</summary>
    public string ItemName => Address;

    /// <summary>
    /// Strongly typed OpenHAB-specific mapping configuration.
    /// </summary>
    public new OpenHabBusMappingConfiguration? Config
    {
        get => base.Config as OpenHabBusMappingConfiguration;
        init => base.Config = value;
    }

    /// <param name="itemName">OpenHAB item name.</param>
    public OpenHabBusEndpointMapping(string itemName, OpenHabBusMappingConfiguration? config = null) : base(BusId, itemName, config) { }
}
