using HomeCompanion.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace HomeCompanion.Logics.Garden;

public sealed class GardenExtension : IExtensionRegistration
{
    public void RegisterServices(IExtensionRegistrationContext context)
    {
        context.Builder.Services.AddSingleton<IRainService, RainService>();
    }
}
