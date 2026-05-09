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
    Task AwaitBusesConnectedAsync(TimeSpan timeout, CancellationToken token = default);
    Task WaitForInitializationStageCompletedAsync(StateInitializationStage level, TimeSpan timeout, CancellationToken token = default);
    Task SignalInitializationStageCompletedAsync(StateInitializationStage level);
}
