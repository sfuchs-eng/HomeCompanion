using Microsoft.Extensions.DependencyInjection;
using SRF.Knx.Config;
using SRF.Knx.Config.OpenHab;
using SRF.Knx.Core;

namespace HomeCompanion.Support.Knx;

public static class KnxSupportHostingHelpers
{
    public static IServiceCollection AddKnxConfigSupport(this IServiceCollection services)
    {
        services.AddKnxCore();
        services.AddKnxConfig();
        services.AddKnxOpenHabConfig();
        services.AddSingleton<KnxValuesCodeGenerator>();
        services.AddSingleton<IHomeCompanionKnxConfigFactory, HomeCompanionKnxConfigFactory>();
        return services;
    }
}