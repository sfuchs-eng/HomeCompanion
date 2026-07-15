namespace HomeCompanion.Base.Diagnostics;

/// <summary>
/// Diagnostic wrapper for an <see cref="IValue"/> instance.
/// </summary>
/// <typeparam name="T">The type of the wrapped <see cref="IValue"/> instance.</typeparam>
public class DiagnosticIValue<T> : IDynamicDiagnosticRecord where T : IValue
{
    private readonly T value;

    public DiagnosticIValue(IDiagnostic owner, string scope, T value)
    {
        this.value = value;
        Owner = owner;
        Scope = scope;
        Value = value;
        Value.Changed += (s, e) => OnChanged();
    }

    public required IDiagnostic Owner { get; init; }
    public required string Scope { get; init; }
    public required string Message { get => value?.ToString() ?? string.Empty; set => throw new NotSupportedException($"Setting the message is not supported. {nameof(DiagnosticIValue<T>)} uses the wrapped IValue's ToString() method."); }

    public T Value => value;

    public string FormattedValue => Value?.Format() ?? string.Empty;

    public event EventHandler<DiagnosticRecordChangedEventArgs>? Changed;

    protected void OnChanged()
    {
        Changed?.Invoke(this, new DiagnosticRecordChangedEventArgs
        {
            Owner = Owner,
            Record = this
        });
    }
}
