namespace HomeCompanion.Base.Model;

public static class BuildingExtensions
{
    public static IEnumerable<BuildingKey> EnumerateBuildingKeys(this Model model)
    {
        foreach (var building in model.Buildings.Values)
        {
            yield return new BuildingKey(building);
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
