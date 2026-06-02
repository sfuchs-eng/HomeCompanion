using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Base.Model;

/// <summary>
/// Configuration for a building.
/// </summary>
public class CfgBuilding : CfgEntity
{
    /// <summary>
    /// The geographical location of the building.
    /// </summary>
    public GeodeticCoordinateWGS84? Location { get; set; }

    /// <summary>
    /// Facades keyed by their configured name.
    /// </summary>
    public Dictionary<string, CfgFacade> Facades { get; set; } = [];

    /// <summary>
    /// Floors keyed by their configured name.
    /// </summary>
    public Dictionary<string, CfgFloor> Floors { get; set; } = [];

    /// <summary>
    /// Specials keyed by their configured name.
    /// This is for any customization that doesn't fit into the facade or floor categories.
    /// Such might be heating systems, solar panels, or other building-wide features that
    /// require configuration and runtime representation.<br/>
    /// Consider whether to use <see cref="ILogic"/> or <see cref="IConfigBackedModelEntity"/> for these.
    /// </summary>
    public Dictionary<string, CfgSpecial> Specials { get; set; } = [];
}

/// <summary>
/// Runtime representation of a building.
/// </summary>
public class Building : ModelEntity
{
    /// <summary>
    /// Facades keyed by their configured name.
    /// </summary>
    public Dictionary<string, Facade> Facades { get; set; } = [];

    /// <summary>
    /// Floors keyed by their configured name.
    /// </summary>
    public Dictionary<string, Floor> Floors { get; set; } = [];

    /// <summary>
    /// Specials keyed by their configured name.
    /// </summary>
    public Dictionary<string, Special> Specials { get; set; } = [];
}

public class CfgSpecial : CfgEntity
{
}

public class Special : ModelEntity, IConfigBackedModelEntity
{
    public Special(string name, CfgSpecial config)
    {
        Name = name;
        Configuration = config;
    }

    public CfgSpecial Configuration { get; set; }

    CfgEntity IConfigBackedModelEntity.Configuration => Configuration;
}