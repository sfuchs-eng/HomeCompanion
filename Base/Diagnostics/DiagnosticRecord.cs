namespace HomeCompanion.Diagnostics;

public class DiagnosticRecord : IDiagnosticRecord
{
    public DiagnosticRecord(string name, string message, IDiagnosticValue? value = null)
    {
        Name = name;
        Message = message;
        Value = value;
    }

    public virtual string Name { get; init; }

    public virtual string Message { get; init; }

    public virtual IDiagnosticValue? Value { get; init; }
}