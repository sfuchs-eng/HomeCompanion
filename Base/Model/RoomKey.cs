using HomeCompanion.Base.Utilities;
using HomeCompanion.Logics.Shutters;

namespace HomeCompanion.Base.Model;

public class RoomKey : KeyBase
{
    private readonly string _key;
    public override string Key => _key;
    public BuildingKey BuildingKey { get; }
    public string FloorName { get; }
    public string RoomName { get; }
    public string RoomType { get; }

    public RoomKey(BuildingKey buildingKey, Floor floor, Room room)
    {
        BuildingKey = buildingKey;
        FloorName = floor.Name;
        RoomName = room.Name;
        RoomType = room.GetType().Name;
        _key = CreateKey();
    }

    public RoomKey(BuildingKey buildingKey, string floorName, string roomType, string roomName)
    {
        BuildingKey = buildingKey;
        FloorName = floorName;
        RoomName = roomName;
        RoomType = roomType;
        _key = CreateKey();
    }

    private string CreateKey()
    {
        return $"{BuildingKey.Key}/{RoomType}:{FloorName},{RoomName}";
    }

    internal static RoomKey Parse(string roomToken, BuildingKey buildingKey)
    {
        var tokens = roomToken.Split(':');
        if (tokens.Length != 2)
            throw new InvalidDataException($"Invalid RoomKey format, expecting 2 tokens separated by ':': {roomToken}");

        var floorAndRoomNames = tokens[1].Split(',');
        if (floorAndRoomNames.Length != 2)
            throw new InvalidDataException($"Invalid RoomKey format, expecting 2 tokens separated by ',': {roomToken}");

        var floorName = floorAndRoomNames[0];
        var roomName = floorAndRoomNames[1];
        var roomKey = new RoomKey(buildingKey, floorName, tokens[0], roomName);
        return roomKey;
    }
}
