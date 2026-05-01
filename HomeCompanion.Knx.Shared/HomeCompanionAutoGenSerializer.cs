using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeCompanion.Knx.Shared;

/// <summary>
/// Serializes and deserializes the <c>HomeCompanionKnxAutoGen.json</c> mapping file.
/// Keys are KNX 3-level group address strings (e.g. <c>"1/2/3"</c>).
/// </summary>
public static class HomeCompanionAutoGenSerializer
{
    private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serializes the entry dictionary to indented JSON.</summary>
    public static string Serialize(Dictionary<string, HomeCompanionAutoGenEntry> entries)
        => JsonSerializer.Serialize(entries, _options);

    /// <summary>
    /// Deserializes a JSON string to the entry dictionary.
    /// Returns <see langword="null"/> if the JSON is invalid or cannot be deserialized.
    /// </summary>
    public static Dictionary<string, HomeCompanionAutoGenEntry>? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, HomeCompanionAutoGenEntry>>(json, _options);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
