namespace HomeCompanion.Base.Model;

public class ShutterKey : KeyBase
{
    public ShutterKey(RoomKey roomKey, Shutter shutter)
    {
        ShutterName = shutter.Name;
        ShutterType = shutter.GetType().Name;
        RoomKey = roomKey;
        _key = $"{roomKey.Key}/{shutter.GetType().Name}:{shutter.Name}";
    }

    public ShutterKey(RoomKey roomKey, string shutterType, string shutterName) : base()
    {
        RoomKey = roomKey;
        ShutterType = shutterType;
        ShutterName = shutterName;
        _key = $"{roomKey.Key}/{shutterType}:{shutterName}";
    }

    private readonly string _key;
    public override string Key => _key;
    public RoomKey RoomKey { get; }
    public string ShutterType { get; }
    public string ShutterName { get; }

    public static ShutterKey Parse(string shutterKeyString)
    {
        var tokens = shutterKeyString.Split('/');
        if (tokens.Length != 3)
            throw new InvalidDataException($"Invalid ShutterKey format, expecting 3 tokens: {shutterKeyString}");
        
        var buildingToken = tokens[0];
        var roomToken = tokens[1];
        var shutterToken = tokens[2];
        var shutterType = shutterToken.Split(':')[0];
        var shutterName = shutterToken.Split(':')[1];

        var buildingKey = BuildingKey.Parse(buildingToken);
        var roomKey = RoomKey.Parse(roomToken, buildingKey);
        return new ShutterKey(roomKey, shutterType, shutterName);
    }
}
