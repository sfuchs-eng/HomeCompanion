using HomeCompanion.Base.Model;

namespace HomeCompanion.Logics.Shutters;

public class BuildingContext(Building building) : ContextBase<BuildingKey>(new BuildingKey(building))
{
    public BuildingKey BuildingKey => Key;
    public Building Building => building;
    public ShadowingSpecial? ShadowingSpecial => Building.TryGetShadowingSpecial(out var ss) ? ss : null;
}