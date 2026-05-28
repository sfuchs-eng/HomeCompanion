using System;
using HomeCompanion.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SRF.Network.Knx;

namespace HomeCompanion.Integrations.Knx;

public class KnxExtensionRegistration(
    ILogger<KnxExtensionRegistration> logger
) : IExtensionRegistration
{
    private readonly ILogger<KnxExtensionRegistration> logger = logger;

    public void RegisterServices(IExtensionRegistrationContext context)
    {
        context.Builder.Services.AddOptions<KnxIntegrationOptions>()
            .BindConfiguration(KnxIntegrationOptions.SectionName);
        AddKnxConnections(context.Builder.Services, context.Builder.Configuration);
        logger.LogInformation("Registered KNX connections.");
    }

    /// <summary>
    /// Registers KNX/IP Routing stacks for all connection names configured under <c>Knx:Connections</c>.
    /// Falls back to a single connection named <c>"default"</c> with library-default settings if no
    /// connections are configured.
    /// </summary>
    /// <remarks>
    /// Each child key under <c>Knx:Connections</c> becomes a named connection bound from
    /// <c>Knx:Connections:{name}</c> via <see cref="SRF.Network.Knx.KnxConnectionOptions"/>.
    /// Multiple entries allow bridging several independent KNX IP Routing segments simultaneously.
    /// </remarks>
    public static IServiceCollection AddKnxConnections(IServiceCollection services, IConfiguration configuration)
    {
        var children = configuration.GetSection("Knx:Connections").GetChildren().ToList();

        if (children.Count == 0)
        {
            // No connections configured — register a single "default" connection with library defaults.
            services.AddKnxIpRouting("default");
            return services;
        }

        foreach (var child in children)
            services.AddKnxIpRouting(child.Key);

        return services;
    }
}
