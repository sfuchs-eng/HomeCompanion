using System.Numerics;
using HomeCompanion.Base.Diagnostics;

namespace HomeCompanion.Diagnostics;

public class DiagnosticRecord : IDiagnosticRecord
{
    public DiagnosticRecord(string name, IDiagnosticValue? value = null, string? explanation = null)
    {
        Name = name;
        if ( value is null && explanation is not null)
        {
            value = new DiagnosticValue(() => explanation);
            explanation = null;
        }
        Explanation = explanation;
        Value = value;
    }

    public DiagnosticRecord(string name, object? value, string? explanation = null)
    {
        Name = name;
        Explanation = explanation;
        Value = value is null ? new DiagnosticValueWrapper("<null>") : new DiagnosticValueWrapper(value);
    }

    private class DiagnosticValueWrapper(object value) : IDiagnosticValue
    {
        public object Value { get; } = value;

        private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public string FormattedValue
        {
            get
            {
                var valueType = Value.GetType();
                if (valueType.IsPrimitive || valueType == typeof(string) || valueType == typeof(decimal))
                {
                    return Value.ToString() ?? "";
                }
                // try json serialization
                try
                {
                    return System.Text.Json.JsonSerializer.Serialize(this.Value, _jsonOptions);
                }
                catch
                {
                    return Value.ToString() ?? "";
                }
            }
        }
    }

    public virtual string Name { get; init; }

    public virtual string? Explanation { get; init; }

    public virtual IDiagnosticValue? Value { get; init; }
}

public static class DiagnosticRecordExtensions
{
    public static DiagnosticIValue<T> AsDiagnosticRecord<T>(this T value, IDiagnosable owner, string scopeIdentifier)
        where T : IValue
    {
        return DiagnosticIValue<T>.Create(value, owner);
    }

    public static DiagnosticRecord AsDiagnosticRecord<T>(this T value, string name, string? explanation = null) where T : Enum
    {
        return new DiagnosticRecord(name, new DiagnosticValue(() => value.ToString() ?? ""), explanation);
    }

    public static DiagnosticRecord AsDiagnosticRecord<T>(this T value, string name, Func<T, string>? formatter, string? explanation = null)
    {
        return new DiagnosticRecord(name, new DiagnosticValue(() => formatter?.Invoke(value) ?? value?.ToString() ?? ""), explanation);
    }

    public static DiagnosticRecord AsDiagnosticRecord(this object value, string name, string? explanation = null)
    {
        return new DiagnosticRecord(name, value, explanation);
    }
}