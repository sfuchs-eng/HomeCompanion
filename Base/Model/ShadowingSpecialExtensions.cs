namespace HomeCompanion.Base.Model;

public static class ShadowingSpecialExtensions
{
    public static IEnumerable<ShadowingSpecial> EnumerateShadowingSpecials(this Model model)
    {
        foreach (var building in model.Buildings.Values)
        {
            foreach (var special in building.Specials.Values)
            {
                if (special is ShadowingSpecial shadowingSpecial)
                    yield return shadowingSpecial;
            }
        }
    }

    /// <summary>
    /// Gets a single shadowing special for a given building.
    /// There must be only 1 shadowing special per building, otherwise false is returned and the out parameter is null.
    /// </summary>
    /// <param name="building"></param>
    /// <param name="shadowingSpecial"></param>
    /// <returns></returns>
    public static bool TryGetShadowingSpecial(this Building building, out ShadowingSpecial shadowingSpecial)
    {
        shadowingSpecial = building.Specials.Values.OfType<ShadowingSpecial>().SingleOrDefault()!;
        return shadowingSpecial != null;
    }

    public static ShadowingSpecial GetShadowingSpecial(this Building building)
    {
        if (!building.TryGetShadowingSpecial(out var special))
            throw new InvalidOperationException($"Building '{building.Name}' does not contain a shadowing special.");
        return special;
    }

    public static bool TryGetRoomSceneShutterPreset(this RoomContext roomContext, byte scene, out CfgRoomSceneShutterPreset? preset)
    {
        // get building level, override with room level if available
        if (roomContext.Room.Configuration.SceneShutterPresets.TryGetValue(scene, out var roomPreset))
        {
            preset = roomPreset;
            return true;
        }
        if (roomContext.Building.TryGetShadowingSpecial(out var shadowingSpecial) && shadowingSpecial.Configuration.SceneShutterPresets.TryGetValue(scene, out var buildingPreset))
        {
            preset = buildingPreset;
            return true;
        }
        preset = null;
        return false;
    }
}
