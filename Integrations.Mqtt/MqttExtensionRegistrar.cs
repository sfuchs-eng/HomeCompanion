using HomeCompanion.Abstractions;
using HomeCompanion.Events;
using HomeCompanion.Extensions;
using HomeCompanion.Values;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Network.Mqtt;
using SRF.Network.Mqtt.Hosting;

namespace HomeCompanion.Integrations.Mqtt;

/// <summary>
/// Registers the MQTT connectivity providers, one per configured MQTT broker.
/// The connectivity provider is responsible for managing the connection to the MQTT broker, subscribing to topics, and publishing messages as needed.
/// The connectivity provider also maintains the mapping between MQTT topics and HomeCompanion values, which is built at startup based on the configured values and their MQTT topic mappings.
/// The connectivity provider is implemented as a keyed singleton, keyed by the MQTT broker configuration, to ensure that there is only one instance managing the connection and mappings for each broker.
/// </summary>
public class MqttExtensionRegistrar : IExtensionRegistration
{
    public void RegisterServices(IExtensionRegistrationContext context)
    {
        var services = context.Builder.Services;
        var configuration = context.Builder.Configuration;

        services.AddOptions<MqttIntegrationOptions>()
            .BindConfiguration(MqttIntegrationOptions.SectionName);

        var brokerSection = configuration.GetSection($"{MqttIntegrationOptions.SectionName}:Brokers");
        foreach (var broker in brokerSection.GetChildren())
        {
            var brokerName = broker.Key;
            if (string.IsNullOrWhiteSpace(brokerName))
                continue;

            services.AddMqtt(brokerName, broker.GetSection("Connection"));

            services.AddKeyedSingleton<MqttPayloadConverter>(brokerName, static (sp, _) =>
                new MqttPayloadConverter(sp.GetRequiredService<ILogger<MqttPayloadConverter>>()));

            services.AddKeyedSingleton<MqttConnectivityProvider>(brokerName, (sp, _) =>
                new MqttConnectivityProvider(
                    brokerName,
                    sp.GetRequiredKeyedService<IMqttBrokerConnection>(brokerName),
                    sp.GetRequiredService<IOptions<MqttIntegrationOptions>>(),
                    sp.GetRequiredService<IEventPublisher>(),
                    sp.GetRequiredService<IEventSubscriber>(),
                    sp.GetServices<IValuesContainer>(),
                    sp.GetRequiredService<IHomeCompanionLifeCycleSynchronization>(),
                    sp.GetRequiredKeyedService<MqttPayloadConverter>(brokerName),
                    sp.GetRequiredService<TimeProvider>(),
                    sp.GetRequiredService<ILogger<MqttConnectivityProvider>>()));

            services.AddSingleton<IConnectivityProvider>(sp =>
                sp.GetRequiredKeyedService<MqttConnectivityProvider>(brokerName));

            services.AddSingleton<IHostedService>(sp =>
                sp.GetRequiredKeyedService<MqttConnectivityProvider>(brokerName));
        }
    }
}
