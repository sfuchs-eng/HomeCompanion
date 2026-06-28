using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Logics.Shutters;

/// <summary>
/// TODO: this is not a building key, it has become a reference to a building runtime. Consider renaming it to BuildingRuntimeReference or similar. Get the key back to the BuildingKey that can be built purely from string identifiers.
/// Same for RoomKey and ShutterKey, they are now runtime references, not pure keys.
/// </summary>
public class BuildingKey(Building building) : KeyBase
{
    public override string Key => $"{nameof(Building)}:{building.Name}";

    public string BuildingName => building.Name;

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
