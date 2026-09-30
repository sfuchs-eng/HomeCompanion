using HomeCompanion.Integrations.OpenHab;
using Microsoft.Extensions.Hosting;
using SRF.Network.OpenHab;
using SRF.Knx.Config;
using SRF.Knx.Config.OpenHab;
using Microsoft.Extensions.DependencyInjection;
using HomeCompanion.Support.Knx;

namespace HomeCompanion.Support.OpenHab;

public static class OpenHabSupportHostingHelpers
{
    public static IServiceCollection AddOpenHabConfigSupport(this IServiceCollection services)
    {
        services.AddKnxConfigSupport();
        services.AddOpenHabConnector();
        services.AddOptions<OpenHabIntegrationOptions>().BindConfiguration(OpenHabIntegrationOptions.SectionName);
        services.AddSingleton<OpenHabValuesCodeGenerator>();
        services.AddSingleton<IHomeCompanionOpenHabConfigFactory, HomeCompanionOpenHabConfigFactory>();
        return services;
    }
}
