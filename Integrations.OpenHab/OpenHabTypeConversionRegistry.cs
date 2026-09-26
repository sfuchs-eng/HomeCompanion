using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Network.OpenHab.Items;
using System.Globalization;
using System.Text.Json;
using UnitsNet;
using UnitsNet.Units;

namespace HomeCompanion.Integrations.OpenHab;

public sealed class OpenHabTypeConversionRegistry
{
    private static readonly Dictionary<string, (bool? TrueValue, bool? FalseValue)> BuiltInBooleanFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OnOff"] = (true, false),
        ["OpenClosed"] = (false, true),
        ["IncreaseDecrease"] = (true, false),
        ["NextPrevious"] = (true, false),
        ["PlayPause"] = (true, false),
        ["RewindFastforward"] = (true, false),
        ["StopMove"] = (true, false),
        ["UpDown"] = (true, false),
        ["Refresh"] = (true, null),
    };

    private readonly OpenHabIntegrationOptions _options;
    private readonly ILogger<OpenHabTypeConversionRegistry> _logger;
    private readonly Lock _mappingsLock = new();
    private IReadOnlyList<OpenHabTypeMappingDefinition>? _sharedMappings;

    private sealed record BooleanFamily(string Name, bool? TrueValue, bool? FalseValue);

    public OpenHabTypeConversionRegistry(
        IOptions<OpenHabIntegrationOptions> options,
        ILogger<OpenHabTypeConversionRegistry> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool TryConvertValue(string rawState, IValue target, string? stateType, Item? itemMetadata, OpenHabBusMappingConfiguration? localConfig, out object? convertedValue)
    {
        convertedValue = null;

        var effectiveConfig = GetEffectiveConfiguration(localConfig, itemMetadata?.Type, stateType, target.ValueType);
        var effectiveItemType = localConfig?.ItemType ?? itemMetadata?.Type;
        var effectiveStateType = ResolveStateType(stateType, localConfig?.StateType, effectiveItemType, rawState);

        if (TryApplyLiteralMapping(rawState, target, effectiveConfig, out convertedValue))
            return true;

        if (TryConvertBoolean(rawState, target.ValueType, effectiveConfig, effectiveStateType, effectiveItemType, out var boolValue))
        {
            convertedValue = boolValue;
            return true;
        }

        if (TryConvertDateTime(rawState, target.ValueType, effectiveStateType, effectiveItemType, out convertedValue))
            return true;

        if (TryConvertString(rawState, target.ValueType, out convertedValue))
            return true;

        if (TryConvertFloatingNumericToTarget(rawState, target.ValueType, out convertedValue))
            return true;

        if (target.TryParseValue(rawState, out convertedValue, out _, CultureInfo.InvariantCulture))
            return true;

        return false;
    }

    public string FormatOutboundValue(IValue source, object value, Item? itemMetadata, OpenHabBusMappingConfiguration? localConfig)
    {
        var effectiveConfig = GetEffectiveConfiguration(localConfig, itemMetadata?.Type, stateType: null, source.ValueType);
        var effectiveItemType = localConfig?.ItemType ?? itemMetadata?.Type;
        var effectiveStateType = ResolveStateType(localConfig?.StateType, localConfig?.StateType, effectiveItemType, rawState: null);

        if (TryFormatBoolean(value, effectiveConfig, effectiveStateType, effectiveItemType, out var boolState))
            return boolState;

        if (TryFormatLiteralOverride(value, effectiveConfig, out var literalState))
            return literalState;

        if (value is IQuantity quantity)
        {
            if (effectiveConfig?.OutboundQuantityUnitMode == OpenHabOutboundQuantityUnitMode.MappingConfiguredUnit
                && source.Unit is not null
                && TryResolveUnitEnum(source.Unit, out var targetUnit))
            {
                try
                {
                    return quantity.ToUnit(targetUnit).ToString(CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to convert outbound quantity for OpenHAB item '{ItemName}' to configured unit.", source.Name);
                }
            }

            return quantity.ToString(CultureInfo.InvariantCulture);
        }

        if (source.Unit is null)
            return value is IFormattable formattableWithoutUnit
                ? formattableWithoutUnit.ToString(null, CultureInfo.InvariantCulture) ?? value.ToString() ?? string.Empty
                : value.ToString() ?? string.Empty;

        if (value is IFormattable formattable)
        {
            var invariantValue = formattable.ToString(null, CultureInfo.InvariantCulture) ?? value.ToString() ?? string.Empty;
            return $"{invariantValue} {source.Unit.DisplayUnit}";
        }

        return $"{value} {source.Unit.DisplayUnit}";
    }

    private bool TryApplyLiteralMapping(string rawState, IValue target, OpenHabBusMappingConfiguration? config, out object? convertedValue)
    {
        convertedValue = null;

        if (config?.LiteralMappings.TryGetValue(rawState, out var mappedLiteral) != true)
            return false;

        return target.TryParseValue(mappedLiteral ?? string.Empty, out convertedValue, out _, CultureInfo.InvariantCulture);
    }

    private static bool TryConvertString(string rawState, Type targetType, out object? convertedValue)
    {
        convertedValue = null;
        var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (nonNullableType != typeof(string))
            return false;

        convertedValue = rawState;
        return true;
    }

    private static bool TryConvertDateTime(string rawState, Type targetType, string? stateType, string? itemType, out object? convertedValue)
    {
        convertedValue = null;
        var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (!string.Equals(stateType, "DateTime", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(itemType, "DateTime", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (nonNullableType == typeof(DateTimeOffset)
            && DateTimeOffset.TryParse(rawState, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            convertedValue = dto;
            return true;
        }

        if (nonNullableType == typeof(DateTime)
            && DateTime.TryParse(rawState, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            convertedValue = dt;
            return true;
        }

        return false;
    }

    private static bool TryConvertFloatingNumericToTarget(string rawState, Type targetType, out object? convertedValue)
    {
        convertedValue = null;

        var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (!double.TryParse(rawState, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number)
            || !double.IsFinite(number))
        {
            return false;
        }

        if (nonNullableType == typeof(bool))
        {
            convertedValue = Math.Abs(number) > double.Epsilon;
            return true;
        }

        if (!IsIntegralNumericType(nonNullableType))
            return false;

        if (Math.Truncate(number) != number)
            return false;

        return TryConvertIntegralDouble(number, nonNullableType, out convertedValue);
    }

    private static bool IsIntegralNumericType(Type type)
        => type == typeof(byte)
           || type == typeof(sbyte)
           || type == typeof(short)
           || type == typeof(ushort)
           || type == typeof(int)
           || type == typeof(uint)
           || type == typeof(long)
           || type == typeof(ulong);

    private static bool TryConvertIntegralDouble(double number, Type targetType, out object? converted)
    {
        converted = null;

        if (targetType == typeof(byte) && number >= byte.MinValue && number <= byte.MaxValue)
        {
            converted = (byte)number;
            return true;
        }

        if (targetType == typeof(sbyte) && number >= sbyte.MinValue && number <= sbyte.MaxValue)
        {
            converted = (sbyte)number;
            return true;
        }

        if (targetType == typeof(short) && number >= short.MinValue && number <= short.MaxValue)
        {
            converted = (short)number;
            return true;
        }

        if (targetType == typeof(ushort) && number >= ushort.MinValue && number <= ushort.MaxValue)
        {
            converted = (ushort)number;
            return true;
        }

        if (targetType == typeof(int) && number >= int.MinValue && number <= int.MaxValue)
        {
            converted = (int)number;
            return true;
        }

        if (targetType == typeof(uint) && number >= uint.MinValue && number <= uint.MaxValue)
        {
            converted = (uint)number;
            return true;
        }

        if (targetType == typeof(long) && number >= long.MinValue && number <= long.MaxValue)
        {
            converted = (long)number;
            return true;
        }

        if (targetType == typeof(ulong) && number >= ulong.MinValue && number <= ulong.MaxValue)
        {
            converted = (ulong)number;
            return true;
        }

        return false;
    }

    private static bool TryConvertBoolean(string rawState, Type targetType, OpenHabBusMappingConfiguration? config, string? stateType, string? itemType, out bool convertedValue)
    {
        convertedValue = default;
        var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (nonNullableType != typeof(bool))
            return false;

        if (ContainsLiteral(config?.TrueLiterals, rawState))
        {
            convertedValue = true;
            return true;
        }

        if (ContainsLiteral(config?.FalseLiterals, rawState))
        {
            convertedValue = false;
            return true;
        }

        var family = ResolveBooleanFamily(stateType, itemType, rawState);
        if (family is null)
            return false;

        var rawUpper = rawState.ToUpperInvariant();
        if (family.TrueValue is not null && IsMatchingBuiltInTrue(rawUpper, family.Name))
        {
            convertedValue = family.TrueValue.Value;
            return true;
        }

        if (family.FalseValue is not null && IsMatchingBuiltInFalse(rawUpper, family.Name))
        {
            convertedValue = family.FalseValue.Value;
            return true;
        }

        return false;
    }

    private static bool TryFormatBoolean(object value, OpenHabBusMappingConfiguration? config, string? stateType, string? itemType, out string state)
    {
        state = string.Empty;
        if (value is not bool boolValue)
            return false;

        if (boolValue && config?.TrueLiterals.FirstOrDefault(static x => !string.IsNullOrWhiteSpace(x)) is { } configuredTrue)
        {
            state = configuredTrue;
            return true;
        }

        if (!boolValue && config?.FalseLiterals.FirstOrDefault(static x => !string.IsNullOrWhiteSpace(x)) is { } configuredFalse)
        {
            state = configuredFalse;
            return true;
        }

        var family = ResolveBooleanFamily(stateType, itemType, rawState: null);
        if (family is not null)
        {
            state = family.Name switch
            {
                "OpenClosed" => boolValue ? "CLOSED" : "OPEN",
                "IncreaseDecrease" => boolValue ? "INCREASE" : "DECREASE",
                "NextPrevious" => boolValue ? "NEXT" : "PREVIOUS",
                "PlayPause" => boolValue ? "PLAY" : "PAUSE",
                "RewindFastforward" => boolValue ? "REWIND" : "FASTFORWARD",
                "StopMove" => boolValue ? "STOP" : "MOVE",
                "UpDown" => boolValue ? "UP" : "DOWN",
                _ => boolValue ? "ON" : "OFF",
            };
            return true;
        }

        state = boolValue ? "ON" : "OFF";
        return true;
    }

    private static bool TryFormatLiteralOverride(object value, OpenHabBusMappingConfiguration? config, out string state)
    {
        state = string.Empty;
        if (config is null || config.LiteralMappings.Count == 0)
            return false;

        var targetLiteral = value is bool boolean
            ? boolean.ToString().ToLowerInvariant()
            : value.ToString();

        if (string.IsNullOrWhiteSpace(targetLiteral))
            return false;

        foreach (var (rawLiteral, mappedLiteral) in config.LiteralMappings)
        {
            if (!string.Equals(mappedLiteral, targetLiteral, StringComparison.OrdinalIgnoreCase))
                continue;

            state = rawLiteral;
            return true;
        }

        return false;
    }

    private OpenHabBusMappingConfiguration? GetEffectiveConfiguration(OpenHabBusMappingConfiguration? localConfig, string? itemType, string? stateType, Type targetType)
    {
        var sharedConfig = FindSharedMapping(itemType, stateType, targetType);
        if (sharedConfig is null)
            return localConfig;

        if (localConfig is null)
            return sharedConfig;

        return new OpenHabBusMappingConfiguration
        {
            ItemType = localConfig.ItemType ?? sharedConfig.ItemType,
            StateType = localConfig.StateType ?? sharedConfig.StateType,
            ValueFormat = localConfig.ValueFormat ?? sharedConfig.ValueFormat,
            OutboundQuantityUnitMode = localConfig.OutboundQuantityUnitMode,
            LiteralMappings = localConfig.LiteralMappings.Count > 0
                ? new Dictionary<string, string>(localConfig.LiteralMappings, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(sharedConfig.LiteralMappings, StringComparer.OrdinalIgnoreCase),
            TrueLiterals = localConfig.TrueLiterals.Count > 0 ? [.. localConfig.TrueLiterals] : [.. sharedConfig.TrueLiterals],
            FalseLiterals = localConfig.FalseLiterals.Count > 0 ? [.. localConfig.FalseLiterals] : [.. sharedConfig.FalseLiterals],
        };
    }

    private OpenHabBusMappingConfiguration? FindSharedMapping(string? itemType, string? stateType, Type targetType)
    {
        var mappings = GetSharedMappings();
        var match = mappings
            .Select(mapping => new { Mapping = mapping, Score = GetSpecificityScore(mapping, itemType, stateType, targetType) })
            .Where(candidate => candidate.Score >= 0)
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();

        return match?.Mapping;
    }

    private static int GetSpecificityScore(OpenHabTypeMappingDefinition mapping, string? itemType, string? stateType, Type targetType)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(mapping.StateType))
        {
            if (!string.Equals(mapping.StateType, stateType, StringComparison.OrdinalIgnoreCase))
                return -1;
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(mapping.ItemType))
        {
            if (!string.Equals(mapping.ItemType, itemType, StringComparison.OrdinalIgnoreCase))
                return -1;
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(mapping.TargetType))
        {
            if (!MatchesTargetType(mapping.TargetType, targetType))
                return -1;
            score += 8;
        }

        return score;
    }

    private IReadOnlyList<OpenHabTypeMappingDefinition> GetSharedMappings()
    {
        if (_sharedMappings is not null)
            return _sharedMappings;

        lock (_mappingsLock)
        {
            if (_sharedMappings is not null)
                return _sharedMappings;

            _sharedMappings = LoadSharedMappings();
            return _sharedMappings;
        }
    }

    private IReadOnlyList<OpenHabTypeMappingDefinition> LoadSharedMappings()
    {
        var mappingPath = ResolvePath(_options.TypeMappingFile);
        if (mappingPath is null || !File.Exists(mappingPath))
            return [];

        try
        {
            var content = File.ReadAllText(mappingPath);
            var parsed = JsonSerializer.Deserialize<OpenHabTypeMappingFile>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return parsed?.Mappings ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load OpenHAB type mappings from '{MappingPath}'. Continuing without shared type overrides.", mappingPath);
            return [];
        }
    }

    private string? ResolvePath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        if (Path.IsPathRooted(fileName))
            return fileName;

        var folder = string.IsNullOrWhiteSpace(_options.MappingsFolder)
            ? AppContext.BaseDirectory
            : _options.MappingsFolder;

        return Path.Combine(folder, fileName);
    }

    private static bool MatchesTargetType(string configuredTargetType, Type targetType)
    {
        var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        return string.Equals(configuredTargetType, nonNullableType.FullName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(configuredTargetType, nonNullableType.AssemblyQualifiedName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(configuredTargetType, nonNullableType.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(configuredTargetType, GetTypeAlias(nonNullableType), StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetTypeAlias(Type type)
        => Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => "bool",
            TypeCode.Byte => "byte",
            TypeCode.Int16 => "short",
            TypeCode.Int32 => "int",
            TypeCode.Int64 => "long",
            TypeCode.UInt16 => "ushort",
            TypeCode.UInt32 => "uint",
            TypeCode.UInt64 => "ulong",
            TypeCode.Single => "float",
            TypeCode.Double => "double",
            TypeCode.Decimal => "decimal",
            TypeCode.String => "string",
            _ => type == typeof(DateTimeOffset) ? "datetimeoffset" : null,
        };

    private static BooleanFamily? ResolveBooleanFamily(string? stateType, string? itemType, string? rawState)
    {
        if (!string.IsNullOrWhiteSpace(stateType) && BuiltInBooleanFamilies.TryGetValue(stateType, out var stateFamily))
            return new BooleanFamily(stateType, stateFamily.TrueValue, stateFamily.FalseValue);

        var inferredStateType = InferStateType(itemType, rawState);
        if (!string.IsNullOrWhiteSpace(inferredStateType) && BuiltInBooleanFamilies.TryGetValue(inferredStateType, out var inferredFamily))
            return new BooleanFamily(inferredStateType, inferredFamily.TrueValue, inferredFamily.FalseValue);

        return null;
    }

    private static bool TryResolveUnitEnum(ValueUnitInfo unitInfo, out Enum unit)
    {
        unit = default!;

        var quantityInfo = Quantity.Infos.FirstOrDefault(info => string.Equals(info.Name, unitInfo.QuantityName, StringComparison.OrdinalIgnoreCase));
        if (quantityInfo is null)
            return false;

        try
        {
            var parsedUnit = Enum.Parse(quantityInfo.UnitType, unitInfo.UnitName, ignoreCase: true);
            if (parsedUnit is Enum parsedEnum)
            {
                unit = parsedEnum;
                return true;
            }
        }
        catch
        {
        }

        try
        {
            foreach (var candidateUnitInfo in quantityInfo.UnitInfos)
            {
                var abbreviations = UnitAbbreviationsCache.Default.GetAbbreviations(candidateUnitInfo, CultureInfo.InvariantCulture);
                if (!abbreviations.Any(abbreviation => string.Equals(abbreviation, unitInfo.UnitName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (candidateUnitInfo.Value is not Enum unitEnum)
                    continue;

                unit = unitEnum;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static string? ResolveStateType(string? liveStateType, string? configuredStateType, string? itemType, string? rawState)
        => !string.IsNullOrWhiteSpace(liveStateType)
            ? liveStateType
            : !string.IsNullOrWhiteSpace(configuredStateType)
                ? configuredStateType
                : InferStateType(itemType, rawState);

    private static string? InferStateType(string? itemType, string? rawState)
    {
        if (string.IsNullOrWhiteSpace(itemType))
            return ClassifyLiteralStateType(rawState);

        if (itemType.StartsWith("Number:", StringComparison.OrdinalIgnoreCase))
            return "Quantity";

        return itemType switch
        {
            "Switch" => "OnOff",
            "Contact" => "OpenClosed",
            "Number" => "Decimal",
            "String" => "String",
            "DateTime" => "DateTime",
            "Dimmer" => string.Equals(rawState, "ON", StringComparison.OrdinalIgnoreCase) || string.Equals(rawState, "OFF", StringComparison.OrdinalIgnoreCase)
                ? "OnOff"
                : "Percent",
            "Rollershutter" => rawState?.Equals("UP", StringComparison.OrdinalIgnoreCase) == true || rawState?.Equals("DOWN", StringComparison.OrdinalIgnoreCase) == true
                ? "UpDown"
                : rawState?.Equals("STOP", StringComparison.OrdinalIgnoreCase) == true || rawState?.Equals("MOVE", StringComparison.OrdinalIgnoreCase) == true
                    ? "StopMove"
                    : "Percent",
            "Player" => ClassifyLiteralStateType(rawState),
            "Color" => rawState?.Equals("ON", StringComparison.OrdinalIgnoreCase) == true || rawState?.Equals("OFF", StringComparison.OrdinalIgnoreCase) == true
                ? "OnOff"
                : rawState?.Equals("INCREASE", StringComparison.OrdinalIgnoreCase) == true || rawState?.Equals("DECREASE", StringComparison.OrdinalIgnoreCase) == true
                    ? "IncreaseDecrease"
                    : "HSB",
            _ => ClassifyLiteralStateType(rawState),
        };
    }

    private static string? ClassifyLiteralStateType(string? rawState)
    {
        if (string.IsNullOrWhiteSpace(rawState))
            return null;

        return rawState.ToUpperInvariant() switch
        {
            "ON" or "OFF" => "OnOff",
            "OPEN" or "CLOSED" => "OpenClosed",
            "UP" or "DOWN" => "UpDown",
            "STOP" or "MOVE" => "StopMove",
            "PLAY" or "PAUSE" => "PlayPause",
            "NEXT" or "PREVIOUS" => "NextPrevious",
            "REWIND" or "FASTFORWARD" => "RewindFastforward",
            "INCREASE" or "DECREASE" => "IncreaseDecrease",
            "REFRESH" => "Refresh",
            _ => null,
        };
    }

    private static bool ContainsLiteral(IEnumerable<string>? literals, string rawState)
        => literals?.Any(literal => string.Equals(literal, rawState, StringComparison.OrdinalIgnoreCase)) == true;

    private static bool IsMatchingBuiltInTrue(string rawState, string family)
        => family switch
        {
            "OnOff" => rawState == "ON",
            "OpenClosed" => rawState == "OPEN",
            "IncreaseDecrease" => rawState == "INCREASE",
            "NextPrevious" => rawState == "NEXT",
            "PlayPause" => rawState == "PLAY",
            "RewindFastforward" => rawState == "REWIND",
            "StopMove" => rawState == "STOP",
            "UpDown" => rawState == "UP",
            "Refresh" => rawState == "REFRESH",
            _ => false,
        };

    private static bool IsMatchingBuiltInFalse(string rawState, string family)
        => family switch
        {
            "OnOff" => rawState == "OFF",
            "OpenClosed" => rawState == "CLOSED",
            "IncreaseDecrease" => rawState == "DECREASE",
            "NextPrevious" => rawState == "PREVIOUS",
            "PlayPause" => rawState == "PAUSE",
            "RewindFastforward" => rawState == "FASTFORWARD",
            "StopMove" => rawState == "MOVE",
            "UpDown" => rawState == "DOWN",
            _ => false,
        };
}