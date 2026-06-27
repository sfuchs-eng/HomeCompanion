using HomeCompanion.Logics.Shutters;

namespace HomeCompanion.Base.Model;

public class ShutterKey(RoomKey roomKey, Shutter shutter) : KeyBase
{
    public override string Key => $"{RoomKey.Key}/{ShutterConfig.PositionValueReference ?? ShutterConfig.OpenCloseReference ?? throw new InvalidOperationException($"A shutter in room {RoomKey.Key} has no position or open/close reference configured.")}";
    public RoomKey RoomKey { get; } = roomKey;
    public Shutter Shutter { get; } = shutter;
    public CfgShutter ShutterConfig => Shutter.Configuration;

    public override int GetHashCode()
    {
        return HashCode.Combine(RoomKey, Shutter);
    }

    public override string ToString() => Key;

    protected override bool EqualsByModelObjectReference(KeyBase? other)
    {
        if (other is ShutterKey otherShutterKey)
        {
            return this.RoomKey.Equals(otherShutterKey.RoomKey) && ReferenceEquals(this.Shutter, otherShutterKey.Shutter);
        }
        return false;
    }
}

public static class ShutterKeyExtensions
{
    public static IEnumerable<ShutterKey> EnumerateShutters(this Model model)
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
}
