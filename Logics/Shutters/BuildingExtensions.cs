namespace HomeCompanion.Logics.Shutters;

internal static class BuildingExtensions
{
    public static IEnumerable<BuildingContext> EnumerateBuildingContexts(this Model model)
    {
        foreach (var building in model.Buildings.Values)
        {
            yield return new BuildingContext(building);
        }
    }
}
