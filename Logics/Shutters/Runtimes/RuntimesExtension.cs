using HomeCompanion.Base.Utilities;
using HomeCompanion.Events;
using HomeCompanion.Extensions;
using HomeCompanion.Logics.Shutters.ShutterAutomation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HomeCompanion.Logics.Shutters.Runtimes;

public interface IRuntimesProvider
{
    ShutterRuntime GetShutterRuntime(ShutterKey shutterKey);
    ShutterRuntime GetShutterRuntime(string shutterKey);
    bool TryGetShutterRuntime(ShutterKey shutterKey, out ShutterRuntime? shutterRuntime);
    bool TryGetShutterRuntime(string shutterKey, out ShutterRuntime? shutterRuntime);

    RoomRuntime GetRoomRuntime(RoomKey roomKey);
    RoomRuntime GetRoomRuntime(string roomKey);
    bool TryGetRoomRuntime(RoomKey roomKey, out RoomRuntime? roomRuntime);
    bool TryGetRoomRuntime(string roomKey, out RoomRuntime? roomRuntime);
    
    BuildingRuntime GetBuildingRuntime(BuildingKey buildingKey);
    BuildingRuntime GetBuildingRuntime(string buildingKey);
    bool TryGetBuildingRuntime(BuildingKey buildingKey, out BuildingRuntime? buildingRuntime);
    bool TryGetBuildingRuntime(string buildingKey, out BuildingRuntime? buildingRuntime);

    IReadOnlyCollection<ShutterRuntime> GetAllShutterRuntimes();
    IReadOnlyCollection<RoomRuntime> GetAllRoomRuntimes();
    IReadOnlyCollection<BuildingRuntime> GetAllBuildingRuntimes();
}

public class RuntimesExtension : Extensions.IExtensionRegistration
{
    public void RegisterServices(IExtensionRegistrationContext context)
    {
        // RuntimesController is injected as an ILogic, but also implements IRuntimesProvider, so that other logics can access the runtimes.
        context.Builder.Services.AddSingleton<IRuntimesProvider>(sp => sp.GetRequiredService<ShadowingRuntimesController>());

        context.Builder.Services.TryAddSingleton<IQueueFeeder<ShutterAutomationComputationTriggerContext>>(sp =>
            new EventBusQueueFeeder<ShutterAutomationComputationTriggerContext>(
                sp.GetRequiredService<IEventPublisher>(),
                trigger => new ShutterAutomationComputationTriggerEvent { Context = trigger }));
    }
}