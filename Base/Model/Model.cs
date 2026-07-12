namespace HomeCompanion.Base.Model;

/// <summary>
/// Runtime building model created from configuration <see cref="CfgModel"/>.
/// In case you use polymorphism in the configuration, make sure to use <see cref="ModelEntityWithConfig{T}"/> for the corresponding class of the model entity hierarchy as well.
/// </summary>
public class Model : ModelEntityWithConfig<CfgModel>
{
    public Model(CfgModel config) : base("root", config)
    {
    }

    /// <summary>
    /// Buildings keyed by their configured name.
    /// </summary>
    public Dictionary<string, Building> Buildings { get; set; } = [];

    /// <summary>
    /// Specials keyed by their configured name.
    /// This is for any customization that doesn't fit into the building, facade, or floor categories.
    /// Consider whether to use <see cref="ILogic"/> or <see cref="IConfigBackedModelEntity"/> for these.
    /// </summary>
    public Dictionary<string, ISpecial> Specials { get; set; } = [];

    /// <summary>
    /// Enumerates all specials of the specified type <typeparamref name="T"/>.
    /// First the ones from the buildings are enumerated, then the ones from the model root.
    /// </summary>
    public IEnumerable<T> GetAllSpecials<T>() where T : ISpecial
    {
        return Buildings.Values.SelectMany(b => b.Specials.Values.OfType<T>()).Concat(Specials.Values.OfType<T>());
    }
}

/// <summary>
/// Configuration root for the runtime building model.
/// The following extension points are available:
/// <list type="bullet">
/// <item><see cref="CfgModel.Specials"/> and <see cref="Building.Specials"/> for any customization that doesn't fit into the building, facade, or floor categories.</item>
/// <item>Use the polymorphism support of the configuration system, see <see cref="CfgEntity"/>, to add new building, facade, shutter, or floor types.</item>
/// </list>
/// </summary>
public class CfgModel : CfgEntity
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