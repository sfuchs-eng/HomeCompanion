using HomeCompanion.Logics.Shutters;

namespace HomeCompanion.Base.Model;

public static class ShutterExtensions
{
    public static IEnumerable<ShutterKey> EnumerateShutterKeys(this Model model)
    {
        foreach (var building in model.Buildings.Values)
        {
            var buildingKey = new BuildingKey(building);
            foreach (var floor in building.Floors.Values)
            {
                foreach (var room in floor.Rooms.Values)
                {
                    var roomKey = new RoomKey(buildingKey, floor, room);
                    foreach (var shutter in room.Shutters.Values)
                    {
                        yield return new ShutterKey(roomKey, shutter);
                    }
                }
            }
        }
    }

    public static Shutter GetShutter(this Model model, ShutterKey shutterKey)
    {
        var building = model.GetBuilding(shutterKey.RoomKey.BuildingKey);
        var floor = building.Floors[shutterKey.RoomKey.FloorName];
        var room = floor.Rooms[shutterKey.RoomKey.RoomName];
        if (room.Shutters.TryGetValue(shutterKey.ShutterName, out var shutter))
        {
            return shutter;
        }
        throw new KeyNotFoundException($"Shutter with key {shutterKey.Key} not found in model.");
    }

    public static IEnumerable<ShutterContext> EnumerateShutterContexts(this Model model)
    {
        foreach (var building in model.Buildings.Values)
        {
            var buildingKey = new BuildingKey(building);
            foreach (var floor in building.Floors.Values)
            {
                foreach (var room in floor.Rooms.Values)
                {
                    var roomKey = new RoomKey(buildingKey, floor, room);
                    foreach (var shutter in room.Shutters.Values)
                    {
                        yield return new ShutterContext(model, roomKey, shutter);
                    }
                }
            }
        }
    }
}
