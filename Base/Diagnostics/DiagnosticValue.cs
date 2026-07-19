using HomeCompanion.Diagnostics;

namespace HomeCompanion.Base.Diagnostics;

/// <summary>
/// Diagnostic wrapper for an <see cref="IValue"/> instance.
/// </summary>
/// <typeparam name="T">The type of the wrapped <see cref="IValue"/> instance.</typeparam>
public class DiagnosticIValue<T> : IDynamicDiagnosticRecord where T : IValue
{
    private readonly T value;

    protected DiagnosticIValue(T value)
    {
        this.value = value;
        this.value.Changed += HandleValueChanged;
    }

    public static DiagnosticIValue<T> Create(T value, IDiagnosable owner)
    {
        return new DiagnosticIValue<T>(value)
        {
            Owner = owner
        };
    }

    public required IDiagnosable Owner { get; init; }

    public T Value => value;

    public string FormattedValue => Value?.Format() ?? string.Empty;

    public string Name => Value?.Name ?? string.Empty;

    public string? Explanation => $"{Value.GetType().Name} {Value?.Label ?? Value?.Name}";

    IDiagnosticValue? IDiagnosticRecord.Value => new DiagnosticValue(() => FormattedValue);

    public event EventHandler<DiagnosticRecordChangedEventArgs>? Changed;

    private void HandleValueChanged(object? sender, ValueChangedEventArgs e)
    {
        Changed?.Invoke(this, new DiagnosticRecordChangedEventArgs
        {
            Owner = Owner,
            Record = this,
            TimeStamp = e.Timestamp
        });
    }
}

public class DiagnosticValue : IDiagnosticValue
{
    private readonly Func<string> valueProvider;

    public DiagnosticValue(Func<string> valueProvider)
    {
        this.valueProvider = valueProvider;
    }

    public string FormattedValue => valueProvider();
}