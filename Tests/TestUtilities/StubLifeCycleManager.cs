using HomeCompanion.Abstractions;

namespace HomeCompanion.Tests.TestUtilities;

internal class StubLifeCycleManager(
    bool isInitializationStageCompleted = true,
    bool isAllUpToStageCompleted = true)
    : IHomeCompanionLifeCycleSynchronization
{
    public virtual AppInitializationStage LastCompletedStage => throw new NotImplementedException();

    public event EventHandler<AppInitializationStageCompletedEventArgs>? InitializationStageCompleted;

    public virtual Task AwaitBusesConnectedAsync(TimeSpan timeout, CancellationToken token = default)
    {
        return Task.CompletedTask;
    }

    public virtual bool IsAllUpToStageCompleted(AppInitializationStage level)
    {
        return isAllUpToStageCompleted;
    }

    public virtual bool IsLifeCycleStageCompleted(AppInitializationStage level)
    {
        return isInitializationStageCompleted;
    }

    public virtual Task SignalInitializationStageCompletedAsync(AppInitializationStage level, object? signaller = null)
    {
        NotifyInitializationStageCompleted(level);
        return Task.CompletedTask;
    }

    public virtual void RegisterRequiredSignaller(AppInitializationStage level, object signaller)
    {
    }

    public virtual Task WaitForInitializationStageCompletedAsync(AppInitializationStage level, TimeSpan timeout, CancellationToken token = default)
    {
        return Task.CompletedTask;
    }

    protected void NotifyInitializationStageCompleted(AppInitializationStage level)
    {
        InitializationStageCompleted?.Invoke(this, new AppInitializationStageCompletedEventArgs(level));
    }

    public virtual void RegisterRequiredExecution(AppInitializationStage targetLevel, Func<AppInitializationStage, CancellationToken, Task> execution)
    {
        // nop
    }
}
