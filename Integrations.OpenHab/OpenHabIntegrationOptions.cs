namespace HomeCompanion.Integrations.OpenHab;

/// <summary>
/// Options controlling OpenHAB to HomeCompanion initialization behavior.
/// </summary>
public sealed class OpenHabIntegrationOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "OpenHabIntegration";

    /// <summary>
    /// If true, values are also initialized by matching OpenHAB item names against
    /// value property names in <see cref="IValuesContainer"/> instances.
    /// </summary>
    public bool EnablePropertyNameMatching { get; set; } = true;

    /// <summary>
    /// File name of the optional JSON state mapping dictionary, located in
    /// <c>Knx:OpenHab:TemplatesFolder</c>.
    /// </summary>
    /// <remarks>
    /// Expected format: { "ON": "true", "OFF": "false" }.
    /// </remarks>
    public string StateMapFile { get; set; } = "OpenHabStateMapping.json";
}
