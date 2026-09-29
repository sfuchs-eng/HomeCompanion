using HomeCompanion.Base.Utilities;
using HomeCompanion.Events;
using HomeCompanion.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HomeCompanion.Logics.Shutters;

public interface IRuntimesProvider
{
    public IReadOnlyDictionary<BuildingKey, BuildingRuntime> BuildingRuntimes { get; }
    public IReadOnlyDictionary<RoomKey, RoomRuntime> RoomRuntimes { get; }
    public IReadOnlyDictionary<ShutterKey, ShutterRuntime> ShutterRuntimes { get; }

    ShutterRuntime GetShutterRuntime(ShutterKey shutterKey);
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