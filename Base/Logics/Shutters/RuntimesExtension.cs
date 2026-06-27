using HomeCompanion.Base.Model;
using HomeCompanion.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace HomeCompanion.Logics.Shutters;

public interface IRuntimesProvider
{
    public IReadOnlyDictionary<BuildingKey, BuildingRuntime> BuildingRuntimes { get; }
    public IReadOnlyDictionary<RoomKey, RoomRuntime> RoomRuntimes { get; }
    public IReadOnlyDictionary<ShutterKey, ShutterRuntime> ShutterRuntimes { get; }
}

public class RuntimesExtension : Extensions.IExtensionRegistration
{
    public void RegisterServices(IExtensionRegistrationContext context)
    {
        // RuntimesController is injected as an ILogic, but also implements IRuntimesProvider, so that other logics can access the runtimes.
        context.Builder.Services.AddSingleton<IRuntimesProvider>(sp => sp.GetRequiredService<RuntimesController>());
    }
}