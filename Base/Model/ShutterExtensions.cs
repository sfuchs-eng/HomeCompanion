using HomeCompanion.Base.Utilities;
using HomeCompanion.Logics.Shutters;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Base.Model;

public static class ShutterExtensions
{
    public static IEnumerable<ShutterKey> EnumerateShutterKeys(this Model model)
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

    public static Shutter GetShutter(this Model model, ShutterKey shutterKey)
    {
        var building = model.GetBuilding(shutterKey.RoomKey.BuildingKey);
        var floor = building.Floors[shutterKey.RoomKey.FloorName];
        var room = floor.Rooms[shutterKey.RoomKey.RoomName];
        if (room.Shutters.TryGetValue(shutterKey.ShutterName, out var shutter))
        {
            return shutter;
        }
        throw new KeyNotFoundException($"Shutter with key {shutterKey.Key} not found in model.");
    }

    public static IEnumerable<ShutterContext> EnumerateShutterContexts(this Model model)
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
                        yield return new ShutterContext(model, roomKey, shutter);
                    }
                }
            }
        }
    }

    public static byte ResolveRoomShutterScene(this Shutter shutter, ShutterRuntimeContext runtimeContext, ILogger? logger = null)
    {
        // Room level
        if (runtimeContext.Room?.ShutterScene?.TryGetValue(out byte scene) ?? false)
        {
            return scene;
        }

        // see whether we can use the building global scene as fallback
        if ((runtimeContext.Building?.TryGetShadowingSpecial(out var shadowingSpecial) ?? false) && (shadowingSpecial.GlobalShutterScene?.TryGetValue(out byte buildingScene) ?? false))
        {
            return buildingScene;
        }

        logger?.LogWarning("No shutter scene found for shutter {ShutterKey} in room {RoomKey}. Using default scene.", runtimeContext.ShutterKey, runtimeContext.RoomKey);

        return (byte)RoomShutterScene.AutoNoReopen; // default scene
    }

    public static ShutterConstraints ResolveEffectiveConstraints(this Shutter shutter, Building? building, Room? room)
    {
        var buildingConstraints = building?.GetShadowingSpecial().Configuration.DefaultShutterConstraints ?? ShutterConstraints.None;
        var roomConstraints = room?.Configuration.ShutterConstraints ?? ShutterConstraints.None;
        var roomMask = room?.Configuration.BuildingConstraintsMask ?? ShutterConstraints.None;
        var shutterConstraints = shutter.Configuration.Constraints;
        var shutterMask = shutter.Configuration.RoomConstraintsMask ?? ShutterConstraints.None;

        return (((buildingConstraints & ~roomMask) | roomConstraints) & ~shutterMask) | shutterConstraints;
    }

    public static CfgDynamicCutoverAngleRule ResolveEffectiveCutoverAngleRule(this Shutter shutter, Building building, Room room)
    {
        ArgumentNullException.ThrowIfNull(building, nameof(building));
        ArgumentNullException.ThrowIfNull(room, nameof(room));

        // Room-level rules override building-level rules
        IEnumerable<CfgDynamicCutoverAngleRule> roomRules = room.Configuration.FacadeSunCutoverAngleDynamicRules;
        IEnumerable<CfgDynamicCutoverAngleRule> shutterRules = shutter.Configuration.FacadeSunCutoverAngleDynamicRules;
        IEnumerable<CfgDynamicCutoverAngleRule> buildingRules = building.GetShadowingSpecial().Configuration.FacadeSunCutoverAngleDynamicRules;

        // Combine rules: room rules take precedence over shutter rules, which take precedence over building rules
        var effectiveRules = new List<CfgDynamicCutoverAngleRule>();
        effectiveRules.AddRange(buildingRules);
        effectiveRules.AddRange(shutterRules);
        effectiveRules.AddRange(roomRules);

        var thermalControlMode = building.GetShadowingSpecial()?.ResolvedThermalControlMode() ?? ThermalControlMode.Passive;
        var roomTemperature = room.GetRoomTemperatureOrDefault();
        var winningRule = (roomRules.Concat(shutterRules).Concat(buildingRules))
            .Where(r => r.Matches(thermalControlMode, roomTemperature))
            .FirstOrDefault();

        if (winningRule is null)
        {
            // No matching rule found, return a default rule with the shutter's configured cut-over angle
            return new CfgDynamicCutoverAngleRule
            {
                CutoverAngle = shutter.Configuration.FacadeSunCutoverAngleOverride ?? 15.0,
                CutoverAngleMax = 45.0,
                ThermalControlMode = thermalControlMode,
                RoomTemperatureMin = 20.0,
                RoomTemperatureMax = 23.0
            };
        }

        // Return the first applicable rule, or null if none are applicable
        return winningRule;
    }

    public static SphericVector GetOrientationRad(this Shutter shutter)
    {
        var orientation = shutter.Facade?.OrientationRad ?? throw new InvalidOperationException($"Shutter {shutter.Name} does not have a facade bound. Cannot compute spheric vector.");
        return orientation;
    }
}
