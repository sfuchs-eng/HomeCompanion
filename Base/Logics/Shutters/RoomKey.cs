using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Logics.Shutters;

public class RoomKey(BuildingKey buildingKey, Floor floor, Room room) : KeyBase
{
    public override string Key => $"{nameof(Room)}:{Building.Name}/{Floor.Name}/{Room.Name}";
    public BuildingKey BuildingKey { get; } = buildingKey;
    public Building Building => BuildingKey.Building;
    public Floor Floor { get; } = floor;
    public Room Room { get; } = room;

    private ShadowingSpecial? shadowingSpecial;
    public ShadowingSpecial ShadowingSpecial => shadowingSpecial ??= Building.TryGetShadowingSpecial(out var ss) ? ss : throw new InvalidOperationException($"Cannot get shadowing special for room {Key} because the building {Building.Name} has no shadowing special configured, which is required for the shutter controller to function properly.");

    protected override bool EqualsByModelObjectReference(KeyBase? other)
    {
        if (other is RoomKey otherRoomKey)
        {
            return this.Building.Equals(otherRoomKey.Building) && this.Floor.Equals(otherRoomKey.Floor) && this.Room.Equals(otherRoomKey.Room);
        }
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Building, Floor, Room);
    }

    public override string ToString() => Key;
}

public static class RoomKeyExtensions
{
    public static IEnumerable<RoomKey> EnumerateRooms(this Model model)
    {
        foreach (var building in model.Buildings.Values)
        {
            var buildingKey = new BuildingKey(building);
            foreach (var floor in building.Floors.Values)
            {
                foreach (var room in floor.Rooms.Values)
                {
                    yield return new RoomKey(buildingKey, floor, room);
                }
            }
        }
    }
}