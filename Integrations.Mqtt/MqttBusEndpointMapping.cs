using HomeCompanion.Values;
using System.Globalization;
using System.Text;

namespace HomeCompanion.Integrations.Mqtt;

/// <summary>
/// Maps an <see cref="IValue"/> to MQTT topics for one configured broker.
/// </summary>
/// <remarks>
/// Add this mapping to <see cref="IValue.BusMappings"/> under key <see cref="BusId"/>.
/// </remarks>
public sealed class MqttBusEndpointMapping : ValueBusMapping<string, string>
{
    /// <summary>
    /// Prefix used to derive a concrete bus id for a broker.
    /// </summary>
    public const string BusIdPrefix = "mqtt://";

    /// <summary>
    /// Gets the concrete bus id for a broker name.
    /// </summary>
    public static string GetBusId(string brokerName) => $"{BusIdPrefix}{brokerName}";

    /// <summary>
    /// Name of the configured broker.
    /// </summary>
    public string BrokerName { get; }

    /// <summary>
    /// Primary inbound state topic filter (can be exact topic or wildcard filter).
    /// </summary>
    public string StateTopicFilter => Address;

    /// <summary>
    /// Optional command topic used for outbound writes and optional inbound command semantics.
    /// </summary>
    public string? CommandTopic { get; init; }

    /// <summary>
    /// Optional additional inbound state topic filters.
    /// </summary>
    public List<string> AdditionalStateTopicFilters { get; init; } = [];

    /// <summary>
    /// Gets all inbound state topic filters.
    /// </summary>
    public IEnumerable<string> GetAllStateTopicFilters()
    {
        yield return StateTopicFilter;
        foreach (var topic in AdditionalStateTopicFilters)
        {
            if (!string.IsNullOrWhiteSpace(topic))
                yield return topic;
        }
    }

    /// <summary>
    /// Initializes a new mapping.
    /// </summary>
    public MqttBusEndpointMapping(
        string brokerName,
        string stateTopicFilter,
        string? commandTopic = null,
        MqttBusMappingConfiguration? config = null)
        : base(GetBusId(brokerName), stateTopicFilter, config ?? new MqttBusMappingConfiguration())
    {
        BrokerName = brokerName;
        CommandTopic = commandTopic;
    }

    /// <summary>
    /// Strongly typed mapping configuration.
    /// </summary>
    public new MqttBusMappingConfiguration? Config
    {
        get => base.Config as MqttBusMappingConfiguration;
        init => base.Config = value;
    }

    /// <inheritdoc/>
    public override bool CanFormatValueForDisplay => true;

    /// <inheritdoc/>
    public override string? FormatValueForDisplay(object? value, CultureInfo? culture = null)
    {
        if (value is null)
            return null;

        culture ??= CultureInfo.CurrentCulture;
        if (value is IFormattable formattable)
            return formattable.ToString(null, culture);

        return value.ToString();
    }
}

/// <summary>
/// Payload encoding mode for MQTT mapping.
/// </summary>
public enum MqttPayloadFormat
{
    /// <summary>
    /// Treat payload as raw UTF-8 text.
    /// </summary>
    RawUtf8,

    /// <summary>
    /// Treat payload as JSON object/array and deserialize to target type.
    /// </summary>
    Json,

    /// <summary>
    /// Treat payload as JSON scalar (or scalar property selected by <see cref="MqttBusMappingConfiguration.JsonPath"/>).
    /// </summary>
    JsonScalar,
}

/// <summary>
/// Polymorphic derived type allow-list entry.
/// </summary>
public sealed class MqttDerivedTypeConfiguration
{
    /// <summary>
    /// Derived type to allow for polymorphic deserialization.
    /// </summary>
    public required Type DerivedType { get; init; }

    /// <summary>
    /// Type discriminator value used in payloads.
    /// </summary>
    public required string Discriminator { get; init; }
}

/// <summary>
/// MQTT-specific mapping configuration.
/// </summary>
public sealed class MqttBusMappingConfiguration : IBusMappingConfiguration
{
    /// <summary>
    /// Payload format used for conversion.
    /// </summary>
    public MqttPayloadFormat PayloadFormat { get; init; } = MqttPayloadFormat.RawUtf8;

    /// <summary>
    /// Optional dotted JSON path used to select a nested payload property.
    /// </summary>
    public string? JsonPath { get; init; }

    /// <summary>
    /// Type discriminator property name for configured polymorphic deserialization.
    /// </summary>
    public string? TypeDiscriminatorProperty { get; init; }

    /// <summary>
    /// Allow-listed derived types for polymorphic deserialization.
    /// </summary>
    public List<MqttDerivedTypeConfiguration> DerivedTypes { get; init; } = [];

    /// <summary>
    /// Optional parameter names bound to wildcard captures from matched topic filters.
    /// </summary>
    public List<string> TopicParameters { get; init; } = [];

    /// <summary>
    /// Optional outbound topic template when <see cref="MqttBusEndpointMapping.CommandTopic"/> is not set.
    /// Supports token <c>{ValueName}</c>.
    /// </summary>
    public string? OutboundTopicTemplate { get; init; }

    /// <summary>
    /// Optional MQTT QoS (0..2) for outbound publishes.
    /// </summary>
    public int? Qos { get; init; }

    /// <summary>
    /// Optional retain flag for outbound publishes.
    /// </summary>
    public bool? Retain { get; init; }

    /// <summary>
    /// Optional content type metadata for payloads.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// If true, inbound echoes of own outbound publishes are ignored when possible.
    /// </summary>
    public bool IgnoreOwnPublishes { get; init; } = true;

    /// <summary>
    /// If true, JSON deserialization fails on unknown properties.
    /// </summary>
    public bool StrictJson { get; init; }

    /// <summary>
    /// Optional deterministic route priority, higher wins.
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// If true, enums are emitted as numeric values on outbound conversion.
    /// </summary>
    public bool EnumAsNumeric { get; init; }

    /// <summary>
    /// Optional custom boolean true literals.
    /// </summary>
    public List<string> TrueLiterals { get; init; } = [];

    /// <summary>
    /// Optional custom boolean false literals.
    /// </summary>
    public List<string> FalseLiterals { get; init; } = [];

    /// <inheritdoc/>
    public string? ValueFormat => null;

    /// <inheritdoc/>
    public string? FormatConfiguration()
    {
        var builder = new StringBuilder();
        builder.Append(PayloadFormat);
        if (!string.IsNullOrWhiteSpace(JsonPath))
            builder.Append($", path={JsonPath}");
        if (Qos is not null)
            builder.Append($", qos={Qos}");
        if (Retain is not null)
            builder.Append($", retain={Retain}");
        if (Priority != 0)
            builder.Append($", priority={Priority}");
        return builder.ToString();
    }
}
