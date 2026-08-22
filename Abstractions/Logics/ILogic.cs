using System;
using HomeCompanion.Values;

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
public interface ILogic : IParametersContainer
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

    /// <summary>
    /// Called up on application termination to allow the logic to clean up resources, unsubscribe from events, and perform any necessary shutdown procedures such as e.g. saving state or flushing queues.
    /// Termination sequence of logics is not guaranteed to be in any specific order, so logics should not depend on other logics being available during termination.
    /// But the rest of the application is normally in a regular, operational state.
    /// It's not guaranteed that <see cref="DisableAsync"/> has been called before termination, so logics should not depend on being disabled during termination.
    /// </summary>
    Task TerminateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables the logic component, allowing it to process events and perform actions.
    /// This method may be called multiple times, but the logic should only be enabled once and subsequent calls should have no effect.
    /// A logic may undergo several enable/disable cycles during its lifetime, e.g. if it is temporarily disabled due to configuration changes or external conditions.
    /// </summary>
    Task EnableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables the logic component, preventing it from processing events and performing actions. The logic may still operate internally,
    /// e.g. to maintain state or perform background tasks, but it will not respond to external partners or trigger actions.
    /// This method may be called multiple times, but the logic should only be disabled once and subsequent calls should have no effect.
    /// </summary>
    Task DisableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <see cref="EnableAsync"/> called successfully, logic operational, and <see cref="DisableAsync"/> not called or enable called again successfully thereafter.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Indicates whether the logic component is activated, that is, whether <see cref="InitializeAsync"/> has been called successfully.
    /// </summary>
    bool IsActivated { get; }

    /// <summary>
    /// Indicates whether the logic component is in a failed state, that is, whether <see cref="InitializeAsync"/> has thrown an exception.
    /// </summary>
    Exception? ActivationException { get; }
}
