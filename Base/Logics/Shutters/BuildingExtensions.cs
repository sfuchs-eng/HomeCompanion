using HomeCompanion.Base.Model;

namespace HomeCompanion.Logics.Shutters;

public static class BuildingExtensions
{
    public static IEnumerable<BuildingKey> EnumerateBuildingKeys(this Model model)
    {
        foreach (var building in model.Buildings.Values)
        {
            yield return new BuildingKey(building);
        }
    }

    public static IEnumerable<BuildingContext> EnumerateBuildingContexts(this Model model)
    {
        foreach (var building in model.Buildings.Values)
        {
            yield return new BuildingContext(building);
        }
    }

    public static Building GetBuilding(this Model model, BuildingKey buildingKey)
    {
        if (model.Buildings.TryGetValue(buildingKey.BuildingName, out var building))
        {
            return building;
        }
        throw new KeyNotFoundException($"Building with key {buildingKey.Key} not found in model.");
    }
}
