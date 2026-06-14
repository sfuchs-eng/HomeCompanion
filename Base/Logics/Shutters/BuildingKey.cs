using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Logics.Shutters;

public class BuildingKey : IEquatable<BuildingKey>, IThingKey
{
    public string Key => Building.Name;
    public Building Building { get; }

    public BuildingKey(Building building)
    {
        this.Building = building;
    }

    public override bool Equals(object? obj)
    {
        return obj is BuildingKey other && Equals(other);
    }

    public bool Equals(BuildingKey? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return this.Building.Equals(other.Building);
    }

    public override int GetHashCode()
    {
        return Building.GetHashCode();
    }

    public override string ToString() => Key;
}