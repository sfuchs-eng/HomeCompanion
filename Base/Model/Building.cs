namespace HomeCompanion.Base.Model;

/// <summary>
/// Configuration for a building.
/// </summary>
public class CfgBuilding : CfgEntity
{
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

public class Special : ModelEntity
{
    public Special(string name, CfgSpecial config)
    {
        Name = name;
        Configuration = config;
    }

    public CfgSpecial Configuration { get; set; }
}