namespace HomeCompanion.Logics.Sun;

public sealed class SunPositionPerBuildingUpdateEvent : IEvent
{
    public required DateTimeOffset Timestamp { get; init; }
    public required IReadOnlyDictionary<BuildingKey, SphericVector> SunPositions { get; init; }
}
