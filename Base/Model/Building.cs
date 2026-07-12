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
    public Dictionary<string, CfgBuildingSpecial> Specials { get; set; } = [];
}

public class CfgBuildingSpecial : CfgSpecial
{
}

/// <summary>
/// Runtime representation of a building.
/// </summary>
public class Building : ModelEntityWithConfig<CfgBuilding>
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
    public Dictionary<string, IBuildingSpecial> Specials { get; set; } = [];

    public Building(string name, CfgBuilding config) : base(name, config)
    {
    }

    public IEnumerable<Room> GetAllRooms()
    {
        foreach (var floor in Floors.Values)
        {
            foreach (var room in floor.Rooms.Values)
            {
                yield return room;
            }
        }
    }

    public ShadowingSpecial GetShadowingSpecial()
    {
        if (TryGetShadowingSpecial(out var shadowingSpecial))
        {
            return shadowingSpecial;
        }
        throw new InvalidOperationException($"Building {Name} has no shadowing special configured, which is required for the shutter controller to function properly.");
    }

    public bool TryGetShadowingSpecial(out ShadowingSpecial shadowingSpecial)
    {
        foreach (var special in Specials.Values)
        {
            if (special is ShadowingSpecial ss)
            {
                shadowingSpecial = ss;
                return true;
            }
        }
        shadowingSpecial = null!;
        return false;
    }
}

public interface IBuildingSpecial : ISpecial
{
}