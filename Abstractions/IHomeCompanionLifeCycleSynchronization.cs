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
    /// Signaling should be idempotent.
    /// </summary>
    Task SignalInitializationStageCompletedAsync(AppInitializationStage level);

    /// <summary>
    /// Returns whether the specified initialization stage has been completed.
    /// </summary>
    bool IsInitializationStageCompleted(AppInitializationStage level);

    /// <summary>
    /// Returns whether all stages up to and including the specified stage are completed.
    /// </summary>
    bool IsAllUpToStageCompleted(AppInitializationStage level);
}
