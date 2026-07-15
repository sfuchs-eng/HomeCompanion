namespace HomeCompanion.Diagnostics;

/// <summary>
/// Represents a key for a diagnostic entity resolution in the tree of diagnostic results.
/// The uniqueness is not guaranteed, but it is expected that the key will be unique within the context of a single diagnostic check.
/// </summary>
/// <remarks>
/// Format: the key is a string with concatenated <see cref="IDiagnosticEntity.Name"/> values separated by the delimiter "/".
/// </remarks>
public interface IDiagnosticEntityKey
{
    string Key { get; }
}

public interface IDiagnosticEntity
{
    string Name { get; }
}

public interface IDiagnosticResultNode : IDiagnosticEntity
{
    IEnumerable<IDiagnosticResultNode> Children { get; }
    IEnumerable<IDiagnosticRecord> Records { get; }
}

/// <summary>
/// Implemented by components that provide diagnostic functionality (e.g. health checks, metrics, debug endpoints).
/// </summary>
/// <remarks>
/// This is a marker interface used for discovery. The diagnostics subsystem will find all registered implementations and invoke them at appropriate times.
/// The exact contract is intentionally left vague for now; it will evolve as we add specific diagnostic requirements.
/// </remarks>
public interface IDiagnosable : IDiagnosticEntity
{
    /// <summary>
    /// Performs the diagnostic check asynchronously and returns a result of type T.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains a <see cref="IDiagnosticResultNode"/> representing the outcome of the diagnostic check.</returns>
    Task<IDiagnosticResultNode> GetDiagnosisAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Diagnostic record represents a single diagnostic message or observation, including its scope and message content.
/// It's computed at the time of the diagnostic check and is immutable. For dynamic diagnostics, a separate interface <see cref="IDynamicDiagnosticRecord"/> is provided to allow for change notifications.
/// </summary>
public interface IDiagnosticRecord : IDiagnosticEntity
{
    string Message { get; }
    IDiagnosticValue? Value { get; }
}

public interface IDiagnosticValue
{
    string FormattedValue { get; }
}

/// <summary>
/// A dynamic diagnostic record may update it's <see cref="IDiagnosticRecord.Message"/> over time, and will notify subscribers of changes via the <see cref="Changed"/> event.
/// </summary>
public interface IDynamicDiagnosticRecord : IDiagnosticRecord
{
    event EventHandler<DiagnosticRecordChangedEventArgs>? Changed;
}

/// <summary>
/// Provides data for the <see cref="IDynamicDiagnosticRecord.Changed"/> event.
/// </summary>
public class DiagnosticRecordChangedEventArgs : EventArgs
{
    public required IDiagnosable Owner { get; init; }
    public required IDynamicDiagnosticRecord Record { get; init; }
    public required DateTimeOffset TimeStamp { get; init; }
}