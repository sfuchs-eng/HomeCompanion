using HomeCompanion.Diagnostics;

namespace HomeCompanion.Base.Diagnostics;

/// <summary>
/// Diagnostic wrapper for an <see cref="IValue"/> instance.
/// </summary>
/// <typeparam name="T">The type of the wrapped <see cref="IValue"/> instance.</typeparam>
public class DiagnosticIValue<T> : IDynamicDiagnosticRecord where T : IValue
{
    private readonly T value;

    public DiagnosticIValue(IDiagnosable owner, string scope, T value)
    {
        this.value = value;
        Owner = owner;
        Scope = scope;
        this.value = value;
        this.value.Changed += HandleValueChanged;
    }

    public required IDiagnosable Owner { get; init; }
    public required string Scope { get; init; }
    public required string Message { get => value?.ToString() ?? string.Empty; set => throw new NotSupportedException($"Setting the message is not supported. {nameof(DiagnosticIValue<T>)} uses the wrapped IValue's ToString() method."); }

    public T Value => value;

    public string FormattedValue => Value?.Format() ?? string.Empty;

    public string Name => Value?.Name ?? string.Empty;

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