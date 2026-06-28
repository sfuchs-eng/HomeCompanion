using HomeCompanion.Logics.Shutters;

namespace HomeCompanion.Base.Model;

public class ShutterContext(Model model, RoomKey roomKey, Shutter shutter) : ContextBase<ShutterKey>(new ShutterKey(roomKey, shutter))
{
    public RoomKey RoomKey => Key.RoomKey;
    public ShutterKey ShutterKey => Key;
    public Building Building => model.GetBuilding(RoomKey.BuildingKey);
    public Floor Floor => Building.Floors[RoomKey.FloorName];
    public Room Room => Floor.Rooms[RoomKey.RoomName];
    public Facade? Facade => shutter.Facade;
    public Shutter Shutter => shutter;
}