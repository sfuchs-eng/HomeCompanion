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
    Task WaitForInitializationStageCompletedAsync(AppLifeCycleStage level, TimeSpan timeout, CancellationToken token = default);

    /// <summary>
    /// Signals that the specified initialization stage has been completed.
    /// Signaling must be idempotent.
    /// </summary>
    /// <remarks>
    /// If a stage has required signallers registered, the stage is only completed when all required signallers have signaled completion of the stage.
    /// If no required signaller is registered, any signaler can complete the stage.
    /// </remarks>
    Task SignalInitializationStageCompletedAsync(AppLifeCycleStage level, object? signaller = null);

    /// <summary>
    /// This method registers the specified signaller for the specified stage as a required signaller to complete that stage.
    /// The stage is only completed when all required signallers have signaled completion of the stage.
    /// If no required signaller is registered, any signaler can complete the stage.
    /// </summary>
    /// <param name="level"></param>
    /// <param name="signaller"></param>
    void RegisterRequiredSignaller(AppLifeCycleStage level, object signaller);

    /// <summary>
    /// Registers the specified execution for the specified initialization stage as a required execution to complete that stage.
    /// The stage is only completed when all required executions have been completed AND all required signallers have signaled completion of the stage.
    /// If no required execution is registered, any (required) signaller can complete the stage instead.
    /// </summary>
    /// <param name="targetLevel">The stage reached after all required callbacks are executed.</param>
    void RegisterRequiredExecution(AppLifeCycleStage targetLevel, Func<AppLifeCycleStage, CancellationToken, Task> execution);

    /// <summary>
    /// Returns whether the specified initialization stage has been completed.
    /// </summary>
    bool IsLifeCycleStageCompleted(AppLifeCycleStage level);

    /// <summary>
    /// Returns whether all stages up to and including the specified stage are completed.
    /// </summary>
    bool IsAllUpToStageCompleted(AppLifeCycleStage level);

    event EventHandler<AppInitializationStageCompletedEventArgs>? InitializationStageCompleted;

    AppLifeCycleStage LastCompletedStage { get; }
}

public class AppInitializationStageCompletedEventArgs : EventArgs
{
    public AppInitializationStageCompletedEventArgs(AppLifeCycleStage stage)
    {
        Stage = stage;
    }

    public AppLifeCycleStage Stage { get; }
}