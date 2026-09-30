using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeCompanion.Integrations.OpenHab;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnitsNet;

namespace HomeCompanion.Support.OpenHab;

public class OpenHabValuesCodeGenerator(
    ILogger<OpenHabValuesCodeGenerator> logger,
    IOptions<OpenHabIntegrationOptions> openHabOptions
)
{
    private readonly ILogger<OpenHabValuesCodeGenerator> logger = logger;
    private readonly OpenHabIntegrationOptions openHabOptions = openHabOptions.Value;

    private readonly Lock mappingsLock = new();
    private IReadOnlyList<OpenHabTypeMappingDefinition>? sharedMappings;

    public string Generate(IEnumerable<OpenHabItemInfo> items, string className = "OpenHabValues", string nameSpace = "HomeCompanion.Local.Values")
    {
        var sb = new StringBuilder();
        sb.AppendLine("using HomeCompanion.Values;");
        sb.AppendLine("using HomeCompanion.Integrations.Knx;");
        sb.AppendLine("using HomeCompanion.Integrations.OpenHab;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine();
        sb.AppendLine($"namespace {nameSpace};");
        sb.AppendLine();
        sb.AppendLine($"public partial class {className}");
        sb.AppendLine("{");

        var usedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var propName = MakeUnique(item.Name, usedNames);
            var summaryText = $"{EscapeXmlComment(item.Name)} (<c>{EscapeXmlComment(item.Type)}</c>)";
            var generationOverride = ResolveGenerationOverride(item);
            string baseType = string.Empty;
            try
            {
                baseType = ResolveBaseType(item, generationOverride);
            }
            catch (NotSupportedException ex)
            {
                logger.LogWarning(ex, "Skipping OpenHAB item '{itemName}' of type '{itemType}' because it is not supported for code generation.", item.Name, item.Type);
                continue;
            }
            if (string.IsNullOrWhiteSpace(baseType))
            {
                logger.LogWarning("Skipping OpenHAB item '{itemName}' of type '{itemType}' because the base type could not be determined.", item.Name, item.Type);
                continue;
            }

            sb.AppendLine($"    /// <summary>{summaryText}</summary>");
            sb.AppendLine($"    public ValueBase<{baseType}> {propName} {{ get; }} = new(loggerFactory.CreateLogger<ValueBase<{baseType}>>())");
            sb.AppendLine("    {");
            sb.AppendLine($"       Name = \"{propName}\",");
            sb.AppendLine($"       Label = {(string.IsNullOrWhiteSpace(item.Name) ? "null" : $"\"{DeCamelize(item.Name)}\"")},");
            if (TryResolveGeneratedUnit(generationOverride, out var generatedUnit))
            {
                var escapedQuantityName = EscapeCSharpString(generatedUnit.QuantityName);
                var escapedUnitName = EscapeCSharpString(generatedUnit.UnitName);
                var unitSymbolLiteral = string.IsNullOrWhiteSpace(generatedUnit.UnitSymbol)
                    ? "null"
                    : $"\"{EscapeCSharpString(generatedUnit.UnitSymbol)}\"";
                sb.AppendLine($"       Unit = new ValueUnitInfo(\"{escapedQuantityName}\", \"{escapedUnitName}\", {unitSymbolLiteral}),");
            }
            sb.AppendLine("       BusMappings = new Dictionary<object, IValueBusEndpointMapping>");
            sb.AppendLine("       {");
            sb.AppendLine($"            [OpenHabBusEndpointMapping.BusId] = new OpenHabBusEndpointMapping(\"{EscapeCSharpString(item.Name)}\", new OpenHabBusMappingConfiguration {{ ItemType = \"{EscapeCSharpString(item.Type)}\", StateType = {(InferStateTypeForItemType(item.Type) is { } stateType ? $"\"{stateType}\"" : "null")} }}) {{ Communication = BusCommunication.Receive | BusCommunication.Transmit }},");
            sb.AppendLine("       }");
            sb.AppendLine("    };");
            sb.AppendLine();
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private string EscapeXmlComment(string name)
    {
        return System.Security.SecurityElement.Escape(name) ?? string.Empty;
    }

    private string MakeUnique(string name, HashSet<string> usedNames)
    {
        var uniqueName = name;
        var counter = 1;
        while (usedNames.Contains(uniqueName))
        {
            uniqueName = $"{name}_{counter}";
            counter++;
        }
        usedNames.Add(uniqueName);
        return uniqueName;
    }

    /// <summary>
    /// Derive a Label from the item Name.
    /// </summary>
    /// <param name="name">The camel-cased name of the OpenHAB item.</param>
    /// <returns>
    /// Rules applied to the input name:
    /// <list type="bullet">
    /// <item>Underscores are replaced with spaces.</item>
    /// <item>Uppercase letters that are alone (not first, no following, no preceding upper case character) are preceded by a space.</item>
    /// </list>
    /// </returns>
    private static string DeCamelize(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (c == '_')
            {
                sb.Append(' ');
                continue;
            }

            if (char.IsUpper(c) && sb.Length > 0 && !char.IsUpper(sb[^1]) && sb[^1] != ' ')
            {
                sb.Append(' ');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private string ResolveBaseType(OpenHabItemInfo item, OpenHabTypeMappingDefinition? generationOverride)
    {
        var overrideType = NormalizeGeneratedValueType(generationOverride?.GeneratedValueType);
        if (!string.IsNullOrWhiteSpace(overrideType))
            return overrideType;

        return GetDefaultBaseType(item.Type);
    }

    private OpenHabTypeMappingDefinition? ResolveGenerationOverride(OpenHabItemInfo item)
    {
        var mappings = GetSharedMappings();
        if (mappings.Count == 0)
            return null;

        var resolved = mappings
            .Select((mapping, index) => new { mapping, index, score = GetGenerationSpecificityScore(mapping, item) })
            .Where(candidate => candidate.score >= 0 && (HasGeneratedValueTypeOverride(candidate.mapping) || HasGeneratedUnitOverride(candidate.mapping)))
            .OrderByDescending(candidate => candidate.score)
            .ThenBy(candidate => candidate.index)
            .FirstOrDefault();

        return resolved?.mapping;
    }

    private static bool HasGeneratedValueTypeOverride(OpenHabTypeMappingDefinition mapping)
        => !string.IsNullOrWhiteSpace(mapping.GeneratedValueType);

    private static bool HasGeneratedUnitOverride(OpenHabTypeMappingDefinition mapping)
        => !string.IsNullOrWhiteSpace(mapping.GeneratedUnitQuantityName)
           && !string.IsNullOrWhiteSpace(mapping.GeneratedUnitName);

    private static bool TryResolveGeneratedUnit(OpenHabTypeMappingDefinition? mapping, out ValueUnitInfo unitInfo)
    {
        unitInfo = default!;
        if (mapping is null)
            return false;

        if (!HasGeneratedUnitOverride(mapping))
            return false;

        unitInfo = new ValueUnitInfo(
            mapping.GeneratedUnitQuantityName!.Trim(),
            mapping.GeneratedUnitName!.Trim(),
            string.IsNullOrWhiteSpace(mapping.GeneratedUnitSymbol) ? null : mapping.GeneratedUnitSymbol.Trim());

        return true;
    }

    private static int GetGenerationSpecificityScore(OpenHabTypeMappingDefinition mapping, OpenHabItemInfo item)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(mapping.ItemName))
        {
            if (!string.Equals(mapping.ItemName, item.Name, StringComparison.OrdinalIgnoreCase))
                return -1;
            score += 100;
        }

        if (!string.IsNullOrWhiteSpace(mapping.ItemNamePattern))
        {
            if (!MatchesPattern(mapping.ItemNamePattern, item.Name))
                return -1;
            score += 80;
        }

        if (!string.IsNullOrWhiteSpace(mapping.ItemType))
        {
            if (!string.Equals(mapping.ItemType, item.Type, StringComparison.OrdinalIgnoreCase))
                return -1;
            score += 60;
        }

        if (!string.IsNullOrWhiteSpace(mapping.ItemTypePattern))
        {
            if (!MatchesPattern(mapping.ItemTypePattern, item.Type))
                return -1;
            score += 40;
        }

        return score;
    }

    private static bool MatchesPattern(string pattern, string input)
    {
        var trimmedPattern = pattern.Trim();
        if (trimmedPattern.Length == 0)
            return false;

        if (trimmedPattern.StartsWith("^", StringComparison.Ordinal) || trimmedPattern.EndsWith("$", StringComparison.Ordinal))
            return Regex.IsMatch(input, trimmedPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (trimmedPattern.Contains('*') || trimmedPattern.Contains('?'))
        {
            var regexPattern = "^" + Regex.Escape(trimmedPattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return Regex.IsMatch(input, trimmedPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private IReadOnlyList<OpenHabTypeMappingDefinition> GetSharedMappings()
    {
        if (sharedMappings is not null)
            return sharedMappings;

        lock (mappingsLock)
        {
            if (sharedMappings is not null)
                return sharedMappings;

            sharedMappings = LoadSharedMappings();
            return sharedMappings;
        }
    }

    private IReadOnlyList<OpenHabTypeMappingDefinition> LoadSharedMappings()
    {
        var mappingPath = ResolvePath(openHabOptions.TypeMappingFile);
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
            logger.LogWarning(ex, "Failed to load OpenHAB type mappings from '{MappingPath}'. Code generation will continue with built-in defaults.", mappingPath);
            return [];
        }
    }

    private string? ResolvePath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        if (Path.IsPathRooted(fileName))
            return fileName;

        var folder = string.IsNullOrWhiteSpace(openHabOptions.MappingsFolder)
            ? AppContext.BaseDirectory
            : openHabOptions.MappingsFolder;

        return Path.Combine(folder, fileName);
    }

    private static string? NormalizeGeneratedValueType(string? configuredType)
    {
        if (string.IsNullOrWhiteSpace(configuredType))
            return null;

        var trimmed = configuredType.Trim();
        return trimmed.StartsWith("global::", StringComparison.Ordinal)
            ? trimmed
            : trimmed switch
            {
                "bool" or "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "float" or "double" or "decimal" or "string" => trimmed,
                _ => $"global::{trimmed}",
            };
    }

    private static string GetDefaultBaseType(string itemType)
    {
        var normalizedType = itemType.Trim();

        return normalizedType switch
        {
            "Switch" => "bool",
            "Contact" => "bool",
            "Dimmer" => "double",
            "Number" => "double",
            var type when type.StartsWith("Number:", StringComparison.OrdinalIgnoreCase) => GetQuantityValueType(type),
            "DateTime" => "global::System.DateTimeOffset",
            "String" => "string",
            "Color" => "string",
            "Location" => "string",
            "Player" => "string",
            "Rollershutter" => "double",
            "Call" => throw new NotSupportedException($"OpenHAB item type '{normalizedType}' handling is not implemented."),
            "Group" => throw new NotSupportedException($"OpenHAB item type '{normalizedType}' handling is not implemented."),
            "Image" => throw new NotSupportedException($"OpenHAB item type '{normalizedType}' handling is not implemented."),
            _ => throw new NotSupportedException($"OpenHAB item type '{normalizedType}' is not supported by the values generator.")
        };
    }

    private static string GetQuantityValueType(string itemType)
    {
        var quantityName = itemType["Number:".Length..].Trim();
        if (string.IsNullOrWhiteSpace(quantityName))
            throw new NotSupportedException($"OpenHAB item type '{itemType}' does not define a quantity dimension.");

        var quantityInfo = Quantity.Infos.FirstOrDefault(info => string.Equals(info.Name, quantityName, StringComparison.OrdinalIgnoreCase));
        if (quantityInfo?.ValueType is null)
            throw new NotSupportedException($"OpenHAB quantity dimension '{quantityName}' is not supported by UnitsNet-based value generation.");

        return quantityInfo.ValueType.FullName is { Length: > 0 } typeName
            ? $"global::{typeName}"
            : $"global::{quantityInfo.ValueType.Name}";
    }

    private static string EscapeCSharpString(string input)
        => input.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string? InferStateTypeForItemType(string itemType)
    {
        var normalizedType = itemType.Trim();

        if (normalizedType.StartsWith("Number:", StringComparison.OrdinalIgnoreCase))
            return "Quantity";

        return normalizedType switch
        {
            "Number" => "Decimal",
            "Switch" => "OnOff",
            "Contact" => "OpenClosed",
            "Dimmer" => "Percent",
            "Rollershutter" => "Percent",
            "DateTime" => "DateTime",
            "String" => "String",
            "Player" => "PlayPause",
            _ => null,
        };
    }
}
