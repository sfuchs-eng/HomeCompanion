using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeCompanion.Abstractions.Serialization;

public sealed class CommaSeparatedFlagsEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly string TypeName = typeof(TEnum).Name;
    private static readonly IReadOnlyDictionary<string, TEnum> ParseValues = BuildParseValues();
    private static readonly IReadOnlyList<(string Name, TEnum Value, ulong RawValue)> WriteValues = BuildWriteValues();
    private static readonly string? ZeroName = WriteValues.FirstOrDefault(value => value.RawValue == 0).Name;

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a string for {TypeName} flags but found {reader.TokenType}.");
        }

        var rawValue = reader.GetString();
        if (rawValue is null)
        {
            throw new JsonException($"Expected a string for {TypeName} flags but found null.");
        }

        var tokens = rawValue.Split(',', StringSplitOptions.None);
        if (tokens.Length == 0)
        {
            throw new JsonException($"Expected at least one flag token for {TypeName}.");
        }

        ulong combined = 0;
        foreach (var token in tokens)
        {
            var trimmed = token.Trim();
            if (trimmed.Length == 0)
            {
                throw new JsonException($"Empty flag token is not valid for {TypeName}.");
            }

            if (!ParseValues.TryGetValue(trimmed, out var parsedValue))
            {
                throw new JsonException($"'{trimmed}' is not a valid {TypeName} flag token.");
            }

            combined |= Convert.ToUInt64(parsedValue);
        }

        return (TEnum)Enum.ToObject(typeof(TEnum), combined);
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        var rawValue = Convert.ToUInt64(value);
        if (rawValue == 0)
        {
            if (ZeroName is null)
            {
                throw new JsonException($"{TypeName} does not define a zero-value flag name.");
            }

            writer.WriteStringValue(ZeroName);
            return;
        }

        var remaining = rawValue;
        var tokens = new List<string>();

        foreach (var flag in WriteValues)
        {
            if (flag.RawValue == 0)
            {
                continue;
            }

            if (!IsSingleFlag(flag.RawValue))
            {
                continue;
            }

            if ((remaining & flag.RawValue) == flag.RawValue)
            {
                tokens.Add(flag.Name);
                remaining &= ~flag.RawValue;
            }
        }

        if (remaining != 0)
        {
            throw new JsonException($"{TypeName} contains unnamed flag bits that cannot be serialized as a string list.");
        }

        if (tokens.Count == 0)
        {
            throw new JsonException($"{TypeName} value '{value}' cannot be serialized as a string list.");
        }

        writer.WriteStringValue(string.Join(", ", tokens));
    }

    private static IReadOnlyDictionary<string, TEnum> BuildParseValues()
    {
        var values = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Enum.GetNames<TEnum>())
        {
            values[name] = Enum.Parse<TEnum>(name, ignoreCase: true);
        }

        return values;
    }

    private static IReadOnlyList<(string Name, TEnum Value, ulong RawValue)> BuildWriteValues()
    {
        var names = Enum.GetNames<TEnum>();
        var values = Enum.GetValues<TEnum>();
        var result = new List<(string Name, TEnum Value, ulong RawValue)>(names.Length);

        for (var index = 0; index < names.Length; index++)
        {
            var value = values[index];
            result.Add((names[index], value, Convert.ToUInt64(value)));
        }

        return result.OrderBy(item => item.RawValue).ToArray();
    }

    private static bool IsSingleFlag(ulong value)
        => value != 0 && (value & (value - 1)) == 0;
}