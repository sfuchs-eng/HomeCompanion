using System;
using HomeCompanion.Persistence;

namespace HomeCompanion.Abstractions;

/// <summary>
/// Interface for synchronizing the life cycle of the Home Companion application.
/// This can be used to coordinate the startup and shutdown processes, ensuring that all components are properly initialized and disposed of in a controlled manner.
/// Targeted towards resolving dependency resolution which cannot be resolved via the DI container,
/// e.g. due to circular dependencies, by allowing components to subscribe to life cycle events and execute their initialization and cleanup logic at the appropriate times.
/// Principle is to await the release of / signal the achievement of initialization / shutdown milestones.
/// </summary>
/// <remarks>
/// This interface is intended to be used by components that need to synchronize their initialization and shutdown processes with the overall life cycle of the Home Companion application.
/// There are 2 main use cases for this interface:
/// <list type="bullet">
/// <item>Inject this interface and register itself as a required signaller for the relevant initialization stages, and then signal completion of those stages when its own initialization is complete.</item>
/// <item>Await the completion of specific initialization stages to ensure that dependent components are fully initialized before proceeding.</item>
/// </list>
/// Pitfall: there needs to be something for each stage that signals the completion of the stage, otherwise the stage will never be completed and any awaiters will wait forever.
/// </remarks>
public interface IHomeCompanionLifeCycleSynchronization
{
    /// <summary>
    /// Waits until all enabled connectivity providers report connected.
    /// This method must not mutate lifecycle state.
    /// </summary>
    Task AwaitBusesConnectedAsync(TimeSpan timeout, CancellationToken token = default);

    /// <summary>
    /// Waits for completion of the specified initialization stage.
    /// This method must not signal or complete stages.
    /// </summary>
    Task WaitForInitializationStageCompletedAsync(AppInitializationStage level, TimeSpan timeout, CancellationToken token = default);

    /// <summary>
    /// Signals that the specified initialization stage has been completed.
    /// Signaling must be idempotent.
    /// </summary>
    /// <remarks>
    /// If a stage has required signallers registered, the stage is only completed when all required signallers have signaled completion of the stage.
    /// If no required signaller is registered, any signaler can complete the stage.
    /// </remarks>
    Task SignalInitializationStageCompletedAsync(AppInitializationStage level, object? signaller = null);

    /// <summary>
    /// This method registers the specified signaller for the specified stage as a required signaller to complete that stage.
    /// The stage is only completed when all required signallers have signaled completion of the stage.
    /// If no required signaller is registered, any signaler can complete the stage.
    /// </summary>
    /// <param name="level"></param>
    /// <param name="signaller"></param>
    void RegisterRequiredSignaller(AppInitializationStage level, object signaller);

    /// <summary>
    /// Returns whether the specified initialization stage has been completed.
    /// </summary>
    bool IsInitializationStageCompleted(AppInitializationStage level);

    /// <summary>
    /// Returns whether all stages up to and including the specified stage are completed.
    /// </summary>
    bool IsAllUpToStageCompleted(AppInitializationStage level);

    event EventHandler<AppInitializationStageCompletedEventArgs>? InitializationStageCompleted;
}

public class AppInitializationStageCompletedEventArgs : EventArgs
{
    public AppInitializationStageCompletedEventArgs(AppInitializationStage stage)
    {
        Stage = stage;
    }

    public AppInitializationStage Stage { get; }
}