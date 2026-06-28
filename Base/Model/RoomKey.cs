using HomeCompanion.Base.Utilities;
using HomeCompanion.Logics.Shutters;

namespace HomeCompanion.Base.Model;

public class RoomKey(BuildingKey buildingKey, Floor floor, Room room) : KeyBase
{
    public override string Key => $"{nameof(Room)}:{BuildingKey.Key}/{floor.Name}/{room.Name}";
    public BuildingKey BuildingKey { get; } = buildingKey;
    public string FloorName => floor.Name;
    public string RoomName => room.Name;
}
