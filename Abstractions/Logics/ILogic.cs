using System;

namespace HomeCompanion.Logics;

/// <summary>
/// Represents a logic component in the HomeCompanion system.
/// A logic component subscribes to events, processes them, and may publish new events or perform actions.
/// The exact contract is intentionally left vague for now; it will evolve as we add specific logic requirements.
/// </summary>
/// <remarks>
/// This is a marker interface used for discovery. The logic subsystem will find all registered implementations and register them as event handlers.
/// Logics are managed as singletons by the host and injected by other logics that depend on them. They are initialized by the host at startup, but may also be initialized on demand by dependent logics before or after being called by the host.
/// Upon initialization, the logic shall be enabled by default.
/// </remarks>
public interface ILogic
{
    string Name { get; }
    /// <summary>
    /// Initializes the logic component, e.g. by subscribing to events and performing any necessary setup.
    /// Might be called multiple times, e.g. also by dependent logics before or after being called by the host.
    /// Parallel calls shall await the first initialization to complete and then return immediately, preventing re-initialization
    /// and allowing dependent logics to call <c>InitializeAsync</c> on their dependencies without risking multiple initializations or deadlocks.
    /// The Logic shall run after this initialization without furthher <see cref="EnableAsync"/> being called, i.e. the logic is expected to be enabled by default after initialization unless configuration prevents it.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task EnableAsync(CancellationToken cancellationToken = default);
    Task DisableAsync(CancellationToken cancellationToken = default);
    bool IsEnabled { get; }
    bool IsActivated { get; }
    Exception? ActivationException { get; }
}
