using HomeCompanion.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace HomeCompanion.Integrations.Sonos;

public sealed class SonosExtensionRegistration : IExtensionRegistration
{
    public void RegisterServices(IExtensionRegistrationContext context)
    {
        var services = context.Builder.Services;

        services.AddSingleton<SonosTransportService>();
        services.AddSingleton<ISonosTransportService>(sp => sp.GetRequiredService<SonosTransportService>());

        services.AddSingleton<SonosDiscoveryService>();
        services.AddSingleton<ISonosDiscoveryService>(sp => sp.GetRequiredService<SonosDiscoveryService>());

        services.AddSingleton<SonosSystemManager>();
        services.AddSingleton<ISonosSystemService>(sp => sp.GetRequiredService<SonosSystemManager>());
        services.AddSingleton<ISonosStateRestoreService>(sp => sp.GetRequiredService<SonosSystemManager>());

        services.AddSingleton<SonosTopologyService>();
        services.AddSingleton<ISonosTopologyService>(sp => sp.GetRequiredService<SonosTopologyService>());

        services.AddSingleton<SonosPlaybackService>();
        services.AddSingleton<ISonosPlaybackService>(sp => sp.GetRequiredService<SonosPlaybackService>());

        services.AddSingleton<SonosQueueService>();
        services.AddSingleton<ISonosQueueService>(sp => sp.GetRequiredService<SonosQueueService>());

        services.AddSingleton<SonosFavoritesService>();
        services.AddSingleton<ISonosFavoritesService>(sp => sp.GetRequiredService<SonosFavoritesService>());

        services.AddSingleton<SonosAlarmService>();
        services.AddSingleton<ISonosAlarmService>(sp => sp.GetRequiredService<SonosAlarmService>());

        services.Configure<HomeCompanionPageRegistrationOptions>(options =>
        {
            options.Pages.Add(new HomeCompanionPageDescriptor
            {
                Title = "Sonos",
                Route = "/sonos",
                Icon = "bi bi-speaker",
                Category = "Media",
                Order = 200,
            });

            options.Assemblies.Add(typeof(HomeCompanion.Integrations.Sonos.SonosPage).Assembly);
        });
    }
}
