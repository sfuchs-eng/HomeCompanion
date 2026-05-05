using System;

namespace HomeCompanion.Logics;

/// <summary>
/// Implemented by components that provide diagnostic functionality (e.g. health checks, metrics, debug endpoints).
/// </summary>
/// <remarks>
/// This is a marker interface used for discovery. The diagnostics subsystem will find all registered implementations and invoke them at appropriate times.
/// The exact contract is intentionally left vague for now; it will evolve as we add specific diagnostic requirements.
/// </remarks>
public interface IDiagnostic
{
}
