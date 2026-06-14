using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Logics.Shutters;

public class ShutterKey(RoomKey roomKey, CfgShutter shutter) : IEquatable<ShutterKey>, IThingKey
{
    public string Key => $"{RoomKey.Key}/{Shutter.PositionValueReference ?? Shutter.OpenCloseReference ?? throw new InvalidOperationException($"A shutter in room {RoomKey.Key} has no position or open/close reference configured.")}";
    public RoomKey RoomKey { get; } = roomKey;
    public CfgShutter Shutter { get; } = shutter;

    public override bool Equals(object? obj)
    {
        return obj is ShutterKey other && Equals(other);
    }

    public bool Equals(ShutterKey? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return this.RoomKey.Equals(other.RoomKey) && this.Shutter.Equals(other.Shutter);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(RoomKey, Shutter);
    }

    public override string ToString() => Key;
}
