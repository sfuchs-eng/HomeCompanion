namespace HomeCompanion.Base.Model;

/// <summary>
/// Runtime building model created from configuration.
/// </summary>
public class Model
{
    /// <summary>
    /// Buildings keyed by their configured name.
    /// </summary>
    public Dictionary<string, Building> Buildings { get; set; } = [];

    /// <summary>
    /// Specials keyed by their configured name.
    /// This is for any customization that doesn't fit into the building, facade, or floor categories.
    /// Consider whether to use <see cref="ILogic"/> or <see cref="IConfigBackedModelEntity"/> for these.
    /// </summary>
    public Dictionary<string, Special> Specials { get; set; } = [];
}

/// <summary>
/// Configuration root for the runtime building model.
/// </summary>
public class CfgModel
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public static string ConfigurationKey => "Model";

    /// <summary>
    /// Optional discriminator key used for polymorphic cfg node materialization.
    /// </summary>
    public static string KindConfigurationKey => "Kind";

    /// <summary>
    /// Buildings keyed by their configured name.
    /// </summary>
    public Dictionary<string, CfgBuilding> Buildings { get; set; } = [];

    public Dictionary<string, CfgSpecial> Specials { get; set; } = [];
}