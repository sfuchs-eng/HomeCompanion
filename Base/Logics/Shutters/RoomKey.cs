using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Logics.Shutters;

public class RoomKey(Building building, Floor floor, Room room) : IEquatable<RoomKey>, IThingKey
{
    public string Key => $"{nameof(Room)}:{Building.Name}/{Floor.Name}/{Room.Name}";

    public Building Building { get; } = building;
    public Floor Floor { get; } = floor;
    public Room Room { get; } = room;

    public override bool Equals(object? obj)
    {
        return obj is RoomKey other && Equals(other);
    }

    public bool Equals(RoomKey? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return this.Building.Equals(other.Building) && this.Floor.Equals(other.Floor) && this.Room.Equals(other.Room);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Building, Floor, Room);
    }

    public override string ToString() => Key;
}
