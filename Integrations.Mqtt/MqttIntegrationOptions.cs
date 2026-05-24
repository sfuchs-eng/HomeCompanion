using SRF.Network.Mqtt;

namespace HomeCompanion.Integrations.Mqtt;

/// <summary>
/// Root options for the MQTT integration.
/// </summary>
public sealed class MqttIntegrationOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Mqtt";

    /// <summary>
    /// Broker configurations keyed by broker name.
    /// </summary>
    public Dictionary<string, MqttBrokerIntegrationOptions> Brokers { get; init; } = [];
}

/// <summary>
/// Configuration for one MQTT broker used by HomeCompanion.
/// </summary>
public sealed class MqttBrokerIntegrationOptions
{
    /// <summary>
    /// Enables or disables the broker integration instance.
    /// </summary>
    public bool Enable { get; init; } = true;

    /// <summary>
    /// Transport connection settings consumed by SRF.Network.Mqtt.
    /// </summary>
    public MqttOptions Connection { get; init; } = new();

    /// <summary>
    /// High-level topic filter subscriptions used as ingress filters.
    /// </summary>
    public List<string> Subscriptions { get; init; } = [];
}
