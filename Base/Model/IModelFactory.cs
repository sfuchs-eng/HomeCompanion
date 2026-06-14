namespace HomeCompanion.Base.Model;

public readonly record struct BuildingCreationContext(Model Model);

public readonly record struct FacadeCreationContext(Model Model, Building Building);

public readonly record struct FloorCreationContext(Model Model, Building Building);

public readonly record struct RoomCreationContext(Model Model, Building Building, Floor Floor);

public readonly record struct ShutterCreationContext(Model Model, Building Building, Floor Floor, Room Room);

public readonly record struct SpecialCreationContext(Model Model, Building? Building);

public interface IModelFactory
{
    CfgModel CreateModelConfig();

    CfgBuilding CreateBuildingConfig(string? kind, string configurationPath);

    CfgFacade CreateFacadeConfig(string? kind, string configurationPath);

    CfgFloor CreateFloorConfig(string? kind, string configurationPath);

    CfgRoom CreateRoomConfig(string? kind, string configurationPath);

    CfgShutter CreateShutterConfig(string? kind, string configurationPath);

    CfgSpecial CreateSpecialConfig(string? kind, string configurationPath);

    Model CreateModel(CfgModel config);

    Building CreateBuilding(BuildingCreationContext context, string name, CfgBuilding config);

    Facade CreateFacade(FacadeCreationContext context, string name, CfgFacade config);

    Floor CreateFloor(FloorCreationContext context, string name, CfgFloor config);

    Room CreateRoom(RoomCreationContext context, string name, CfgRoom config);

    Shutter CreateShutter(ShutterCreationContext context, string name, CfgShutter config);

    ISpecial CreateSpecial(SpecialCreationContext context, string name, CfgSpecial config);
}
