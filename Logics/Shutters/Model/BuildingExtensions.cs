using BaseModel = global::HomeCompanion.Base.Model.Model;

namespace HomeCompanion.Logics.Shutters.Model;

internal static class BuildingExtensions
{
    public static IEnumerable<BuildingContext> EnumerateBuildingContexts(this BaseModel model)
    {
        foreach (var building in model.Buildings.Values)
        {
            yield return new BuildingContext(building);
        }
    }
}
