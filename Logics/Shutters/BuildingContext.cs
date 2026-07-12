namespace HomeCompanion.Logics.Shutters;

public class BuildingContext(Building building) : ContextBase<BuildingKey>(new BuildingKey(building))
{
    public BuildingKey BuildingKey => Key;
    public Building Building => building;
    public ShadowingSpecial? ShadowingSpecial => Building.TryGetShadowingSpecial(out var ss) ? ss : null;

    public IEnumerable<RoomKey> EnumerateRoomKeys()
    {
        foreach (var floor in Building.Floors.Values)
        {
            foreach (var room in floor.Rooms.Values)
            {
                yield return new RoomKey(BuildingKey, floor, room);
            }
        }
    }

    public IEnumerable<ShutterKey> EnumerateShutterKeys()
    {
        foreach (var floor in Building.Floors.Values)
        {
            foreach (var room in floor.Rooms.Values)
            {
                foreach (var shutter in room.Shutters.Values)
                {
                    yield return new ShutterKey(new RoomKey(BuildingKey, floor, room), shutter);
                }
            }
        }
    }
}