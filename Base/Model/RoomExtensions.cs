namespace HomeCompanion.Base.Model;

public static class RoomExtensions
{
    public static IEnumerable<RoomKey> EnumerateRoomKeys(this Model model)
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

    public static Room GetRoom(this Model model, RoomKey roomKey)
    {
        var building = model.GetBuilding(roomKey.BuildingKey);
        var floor = building.Floors[roomKey.FloorName];
        if (floor.Rooms.TryGetValue(roomKey.RoomName, out var room))
        {
            return room;
        }
        throw new KeyNotFoundException($"Room with key {roomKey.Key} not found in model.");
    }

    public static IEnumerable<RoomContext> EnumerateRoomContexts(this Model model)
    {
        foreach (var building in model.Buildings.Values)
        {
            var buildingKey = new BuildingKey(building);
            foreach (var floor in building.Floors.Values)
            {
                foreach (var room in floor.Rooms.Values)
                {
                    yield return new RoomContext(model, buildingKey, floor, room);
                }
            }
        }
    }
}
