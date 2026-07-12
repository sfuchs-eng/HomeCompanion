namespace HomeCompanion.Base.Model;

public class RoomContext(Model model, BuildingKey buildingKey, Floor floor, Room room) : ContextBase<RoomKey>(new RoomKey(buildingKey, floor, room))
{
    public BuildingKey BuildingKey => Key.BuildingKey;
    public RoomKey RoomKey => Key;
    public Building Building => model.GetBuilding(BuildingKey);
    public Floor Floor => floor;
    public Room Room => room;
}