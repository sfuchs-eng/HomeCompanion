using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Logics.Shutters;

public class BuildingKey : KeyBase
{
    public override string Key => Building.Name;
    public Building Building { get; }

    public BuildingKey(Building building)
    {
        this.Building = building;
    }

    public override bool Equals(object? obj)
    {
        return obj is BuildingKey other && Equals(other);
    }

    protected override bool EqualsByModelObjectReference(KeyBase? other)
    {
        if (other is BuildingKey otherBuildingKey)
        {
            return this.Building.Equals(otherBuildingKey.Building);
        }
        return false;
    }

    public override int GetHashCode()
    {
        return Building.GetHashCode();
    }

    public override string ToString() => Key;
}

public static class BuildingKeyExtensions
{
    public static IEnumerable<BuildingKey> EnumerateBuildings(this Model model)
    {
        foreach (var building in model.Buildings.Values)
        {
            yield return new BuildingKey(building);
        }
    }
}
