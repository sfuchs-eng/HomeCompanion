namespace HomeCompanion.Integrations.OpenHab;

/// <summary>
/// Options controlling OpenHAB initialization and type conversion behavior.
/// </summary>
public sealed class OpenHabIntegrationOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "OpenHAB";

    public bool Enable { get; set; } = true;
    
    /// <summary>
    /// If true, values are also initialized by matching OpenHAB item names against
    /// value property names in <see cref="IValuesContainer"/> instances.
    /// </summary>
    public bool EnablePropertyNameMatching { get; set; } = true;

    /// <summary>
    /// Folder containing optional OpenHAB mapping files.
    /// </summary>
    public string MappingsFolder { get; set; } = AppContext.BaseDirectory;

    /// <summary>
    /// File name of the optional JSON state mapping dictionary.
    /// Expected format: { "ON": "true", "OFF": "false" }.
    /// </summary>
    public string StateMapFile { get; set; } = "OpenHabStateMapping.json";

    /// <summary>
    /// File name of the optional shared OpenHAB type mapping registry.
    /// </summary>
    public string TypeMappingFile { get; set; } = "OpenHabTypeMapping.json";
}
