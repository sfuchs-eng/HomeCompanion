namespace HomeCompanion.Base.Model;

public class ShutterKey(RoomKey roomKey, Shutter shutter) : KeyBase
{
    public override string Key => $"{shutter.GetType().Name}:{RoomKey.Key}/{shutter.Configuration.PositionValueReference ?? shutter.Configuration.OpenCloseReference ?? throw new InvalidOperationException($"A shutter in room {RoomKey.Key} has no position or open/close reference configured.")}";
    public RoomKey RoomKey { get; } = roomKey;
    public string ShutterName => shutter.Name;
}
