using System;
using HomeCompanion.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SRF.Network.Knx;

namespace HomeCompanion.Integrations.Knx;

public class KnxExtensionRegistration : IExtensionRegistration
{
    public void RegisterServices(IExtensionRegistrationContext context)
    {
        // Register KNX-specific services here
        AddKnxConnections(context.Builder.Services, context.Builder.Configuration);
    }

    /// <summary>
    /// Registers KNX/IP Routing stacks for all connection names configured under <c>Knx:Connections</c>.
    /// Falls back to a single connection named <c>"default"</c> with library-default UDP settings if no
    /// connections are configured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Knx:Connections</c> is a dictionary where each key is the connection name and the value is the
    /// UDP multicast configuration for that connection (<c>UdpMulticastOptions</c>). The UDP settings are
    /// read directly from <c>Knx:Connections:{name}</c>, so no separate <c>Udp:Connections</c> section is
    /// required. Example:
    /// </para>
    /// <code>
    /// "Knx": {
    ///   "Connections": {
    ///     "default": {
    ///       "MulticastAddress": "224.0.23.12",
    ///       "Port": 3671,
    ///       "ConnectionManager": { "ReconnectInterval": 10.0 }
    ///     }
    ///   }
    /// }
    /// </code>
    /// <para>
    /// Multiple entries allow bridging several independent KNX IP Routing segments simultaneously.
    /// </para>
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
            services.AddKnxIpRouting(child.Key, $"Knx:Connections:{child.Key}");

        return services;
    }
}
