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

    public ShadowingSpecial GetShadowingSpecial()
    {
        if (Building.TryGetShadowingSpecial(out var shadowingSpecial))
        {
            return shadowingSpecial;
        }
        return new ShadowingSpecial("default", new CfgShadowingSpecial()); // Return default if not found
    }

    /// <summary>
    /// Resolves the effective shutter constraints for the shutter within its context,
    /// considering building-level defaults, room-level defaults, and individual shutter constraints.
    /// </summary>
    /// <returns>The resolved <see cref="ShutterConstraints"/> applicable to the shutter</returns>
    public ShutterConstraints ResolveShutterConstraints()
    {
        var buildingConstraints = GetShadowingSpecial().Configuration.DefaultShutterConstraints;
        var roomConstraints = Room.Configuration.ShutterConstraints;
        var roomMask = Room.Configuration.BuildingConstraintsMask ?? ShutterConstraints.None;
        var shutterConstraints = Shutter.Configuration.Constraints;
        var shutterMask = Shutter.Configuration.RoomConstraintsMask ?? ShutterConstraints.None;

        return (((buildingConstraints & ~roomMask) | roomConstraints) & ~shutterMask) | shutterConstraints;
    }
}