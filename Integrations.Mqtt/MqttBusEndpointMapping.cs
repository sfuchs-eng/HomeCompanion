using HomeCompanion.Values;
using System.Globalization;

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
    public override string? FormatValueForDisplay(object? value, CultureInfo? culture = null, IFormatProvider? formatProvider = null, string? format = null)
    {
        if (value is null)
            return null;

        culture ??= CultureInfo.CurrentCulture;
        if (value is IFormattable formattable)
            return formattable.ToString(null, culture ?? CultureInfo.CurrentCulture);

        return value.ToString();
    }
}
