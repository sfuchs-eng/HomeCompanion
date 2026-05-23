using HomeCompanion.Extensions;

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
        throw new NotImplementedException();
    }
}
