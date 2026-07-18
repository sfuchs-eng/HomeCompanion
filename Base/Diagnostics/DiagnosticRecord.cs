using System.Numerics;
using HomeCompanion.Base.Diagnostics;

namespace HomeCompanion.Diagnostics;

public class DiagnosticRecord : IDiagnosticRecord
{
    public DiagnosticRecord(string name, string? message, IDiagnosticValue? value = null)
    {
        Name = name;
        Message = message;
        Value = value;
    }

    public DiagnosticRecord(string name, string? message, object? value)
    {
        Name = name;
        Message = message;
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

    public virtual string? Message { get; init; }

    public virtual IDiagnosticValue? Value { get; init; }
}

public static class DiagnosticRecordExtensions
{
    public static DiagnosticIValue<T> AsDiagnosticRecord<T>(this T value, IDiagnosable owner, string scopeIdentifier)
        where T : IValue
    {
        return DiagnosticIValue<T>.Create(value, owner);
    }

    public static DiagnosticRecord AsDiagnosticRecord<T>(this T value, string name, string? message = null) where T : Enum
    {
        return new DiagnosticRecord(name, message, new DiagnosticValue(() => value.ToString() ?? ""));
    }

    public static DiagnosticRecord AsDiagnosticRecord(this object value, string name, string? message = null)
    {
        return new DiagnosticRecord(name, message, value);
    }
}