using HomeCompanion.Base.Model;

namespace HomeCompanion.Logics.Shutters;

public partial class ShutterController
{
    private class RuntimeContext(ShutterKey shutterKey, BuildingRuntime? buildingRuntime, RoomRuntime? roomRuntime, ShutterRuntime? shutterRuntime)
    {
        public BuildingKey? BuildingKey => BuildingRuntime?.BuildingKey;
        public RoomKey? RoomKey => RoomRuntime?.RoomKey;
        public ShutterKey ShutterKey { get; } = shutterKey;

        public BuildingRuntime? BuildingRuntime { get; } = buildingRuntime;
        public RoomRuntime? RoomRuntime { get; } = roomRuntime;
        public ShutterRuntime? ShutterRuntime { get; } = shutterRuntime;


        public Building? Building => BuildingRuntime?.BuildingContext.Building;
        public Room? Room => RoomRuntime?.RoomContext.Room;
        public Shutter? Shutter => ShutterRuntime?.ShutterContext.Shutter;
    }
}
