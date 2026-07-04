using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Logics.Shutters;

/// <summary>
/// Represents a unique key for a building, used to identify and manage buildings within the HomeCompanion system.
/// </summary>
public class BuildingKey : KeyBase
{
    public BuildingKey(Building building)
    {
        _key = $"{building.GetType().Name}:{building.Name}";
        this.BuildingName = building.Name;
        this.BuildingType = building.GetType().Name;
    }

    public BuildingKey(string buildingType, string buildingName) : base()
    {
        this._key = $"{buildingType}:{buildingName}";
        this.BuildingName = buildingName;
        this.BuildingType = buildingType;
    }

    private readonly string _key;
    override public string Key => _key;

    public string BuildingName { get; }
    public string BuildingType { get; }

    public static BuildingKey Parse(string buildingToken)
    {
        var tokens = buildingToken.Split(':');
        if (tokens.Length != 2)
            throw new InvalidDataException($"Invalid BuildingKey format, expecting 2 tokens separated by ':': {buildingToken}");

        var buildingType = tokens[0];
        var buildingName = tokens[1];
        var buildingKey = new BuildingKey(buildingType, buildingName);
        return buildingKey;
    }

    public override bool Equals(object? obj)
    {
        return obj is BuildingKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Key.GetHashCode();
    }

    public override string ToString() => Key;
}
