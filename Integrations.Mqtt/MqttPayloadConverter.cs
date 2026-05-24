using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace HomeCompanion.Integrations.Mqtt;

internal sealed class MqttPayloadConverter
{
    private static readonly string[] DefaultTrueLiterals = ["true", "on", "open", "enable", "enabled", "1"];
    private static readonly string[] DefaultFalseLiterals = ["false", "off", "closed", "disable", "disabled", "0"];

    private readonly ILogger<MqttPayloadConverter> _logger;

    public MqttPayloadConverter(ILogger<MqttPayloadConverter> logger)
    {
        _logger = logger;
    }

    public bool TryDecode(string payloadUtf8, Type targetType, MqttBusEndpointMapping mapping, out object? value)
    {
        value = null;

        var config = mapping.Config ?? new MqttBusMappingConfiguration();
        var nonNullableTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            switch (config.PayloadFormat)
            {
                case MqttPayloadFormat.RawUtf8:
                    return TryConvertScalar(payloadUtf8, nonNullableTarget, config, out value);

                case MqttPayloadFormat.JsonScalar:
                {
                    using var document = JsonDocument.Parse(payloadUtf8);
                    if (!TrySelectJsonElement(document, config.JsonPath, out var element))
                        return false;

                    return TryConvertJsonElementScalar(element, nonNullableTarget, config, out value);
                }

                case MqttPayloadFormat.Json:
                {
                    using var document = JsonDocument.Parse(payloadUtf8);
                    if (!TrySelectJsonElement(document, config.JsonPath, out var element))
                        return false;

                    if (IsScalarTargetType(nonNullableTarget))
                        return TryConvertJsonElementScalar(element, nonNullableTarget, config, out value);

                    var options = BuildJsonOptions(config, nonNullableTarget);
                    value = JsonSerializer.Deserialize(element.GetRawText(), targetType, options);
                    return true;
                }

                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MQTT payload conversion failed for target type {Type}.", targetType);
            value = null;
            return false;
        }
    }

    public string Encode(object? value, Type declaredType, MqttBusEndpointMapping mapping)
    {
        var config = mapping.Config ?? new MqttBusMappingConfiguration();

        return config.PayloadFormat switch
        {
            MqttPayloadFormat.Json => JsonSerializer.Serialize(value, value?.GetType() ?? declaredType, BuildJsonOptions(config, declaredType)),
            MqttPayloadFormat.JsonScalar => JsonSerializer.Serialize(value, value?.GetType() ?? declaredType, BuildJsonOptions(config, declaredType)),
            _ => ConvertToRawUtf8(value, config),
        };
    }

    private static bool TrySelectJsonElement(JsonDocument document, string? jsonPath, out JsonElement element)
    {
        element = document.RootElement;
        if (string.IsNullOrWhiteSpace(jsonPath))
            return true;

        var pathSegments = jsonPath.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in pathSegments)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out var child))
                return false;

            element = child;
        }

        return true;
    }

    private static bool IsScalarTargetType(Type targetType)
    {
        return targetType.IsPrimitive
            || targetType == typeof(string)
            || targetType == typeof(decimal)
            || targetType.IsEnum
            || targetType == typeof(DateTime)
            || targetType == typeof(DateTimeOffset)
            || targetType == typeof(Guid)
            || targetType == typeof(TimeSpan);
    }

    private static bool TryConvertJsonElementScalar(
        JsonElement element,
        Type targetType,
        MqttBusMappingConfiguration config,
        out object? value)
    {
        value = null;

        if (targetType == typeof(string))
        {
            value = element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
            return true;
        }

        if (targetType == typeof(bool))
        {
            if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = element.GetBoolean();
                return true;
            }

            var raw = element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText();
            return TryConvertBoolean(raw, config, out value);
        }

        if (targetType.IsEnum)
        {
            var enumRaw = element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
            if (enumRaw is null)
                return false;

            if (Enum.TryParse(targetType, enumRaw, ignoreCase: true, out var enumValue))
            {
                value = enumValue;
                return true;
            }

            if (long.TryParse(enumRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
            {
                value = Enum.ToObject(targetType, numeric);
                return true;
            }

            return false;
        }

        if (targetType == typeof(Guid) && element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var guid))
        {
            value = guid;
            return true;
        }

        if (targetType == typeof(TimeSpan) && element.ValueKind == JsonValueKind.String && TimeSpan.TryParse(element.GetString(), CultureInfo.InvariantCulture, out var timespan))
        {
            value = timespan;
            return true;
        }

        if (targetType == typeof(DateTimeOffset) && element.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            value = dto;
            return true;
        }

        if (targetType == typeof(DateTime) && element.ValueKind == JsonValueKind.String && DateTime.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            value = dt;
            return true;
        }

        var rawScalar = element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText();
        return TryConvertScalar(rawScalar, targetType, config, out value);
    }

    private static bool TryConvertScalar(
        string raw,
        Type targetType,
        MqttBusMappingConfiguration config,
        out object? value)
    {
        value = null;

        if (targetType == typeof(string))
        {
            value = raw;
            return true;
        }

        if (targetType == typeof(bool))
            return TryConvertBoolean(raw, config, out value);

        if (targetType.IsEnum)
        {
            if (Enum.TryParse(targetType, raw, ignoreCase: true, out var enumValue))
            {
                value = enumValue;
                return true;
            }

            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
            {
                value = Enum.ToObject(targetType, numeric);
                return true;
            }

            return false;
        }

        try
        {
            value = Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryConvertBoolean(string raw, MqttBusMappingConfiguration config, out object? value)
    {
        value = null;

        var normalized = raw.Trim();
        if (string.IsNullOrEmpty(normalized))
            return false;

        var trueSet = config.TrueLiterals.Count > 0 ? config.TrueLiterals : [.. DefaultTrueLiterals];
        var falseSet = config.FalseLiterals.Count > 0 ? config.FalseLiterals : [.. DefaultFalseLiterals];

        if (trueSet.Any(v => normalized.Equals(v, StringComparison.OrdinalIgnoreCase)))
        {
            value = true;
            return true;
        }

        if (falseSet.Any(v => normalized.Equals(v, StringComparison.OrdinalIgnoreCase)))
        {
            value = false;
            return true;
        }

        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
        {
            value = Math.Abs(numeric) > double.Epsilon;
            return true;
        }

        return false;
    }

    private static JsonSerializerOptions BuildJsonOptions(MqttBusMappingConfiguration config, Type targetType)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = config.StrictJson
                ? JsonUnmappedMemberHandling.Disallow
                : JsonUnmappedMemberHandling.Skip,
        };

        if (config.DerivedTypes.Count == 0)
            return options;

        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo =>
        {
            if (typeInfo.Type != targetType)
                return;

            var polymorphism = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = string.IsNullOrWhiteSpace(config.TypeDiscriminatorProperty)
                    ? "$type"
                    : config.TypeDiscriminatorProperty,
                IgnoreUnrecognizedTypeDiscriminators = false,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
            };

            foreach (var candidate in config.DerivedTypes)
            {
                if (!targetType.IsAssignableFrom(candidate.DerivedType))
                    continue;

                polymorphism.DerivedTypes.Add(new JsonDerivedType(candidate.DerivedType, candidate.Discriminator));
            }

            if (polymorphism.DerivedTypes.Count > 0)
                typeInfo.PolymorphismOptions = polymorphism;
        });

        options.TypeInfoResolver = resolver;
        return options;
    }

    private static string ConvertToRawUtf8(object? value, MqttBusMappingConfiguration config)
    {
        if (value is null)
            return string.Empty;

        if (value is string text)
            return text;

        var valueType = value.GetType();
        var nonNullable = Nullable.GetUnderlyingType(valueType) ?? valueType;

        if (nonNullable == typeof(bool))
            return ((bool)value) ? "true" : "false";

        if (nonNullable.IsEnum)
        {
            if (config.EnumAsNumeric)
                return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

            return value.ToString() ?? string.Empty;
        }

        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;

        return value.ToString() ?? string.Empty;
    }
}
