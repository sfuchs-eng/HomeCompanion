using HomeCompanion.Abstractions;

namespace HomeCompanion.Tests.TestUtilities;

internal class StubLifeCycleManager(
    bool isInitializationStageCompleted = true,
    bool isAllUpToStageCompleted = true)
    : IHomeCompanionLifeCycleSynchronization
{
    public virtual AppLifeCycleStage LastCompletedStage => throw new NotImplementedException();

    public event EventHandler<AppInitializationStageCompletedEventArgs>? InitializationStageCompleted;

    public virtual Task AwaitBusesConnectedAsync(TimeSpan timeout, CancellationToken token = default)
    {
        return Task.CompletedTask;
    }

    public virtual bool IsAllUpToStageCompleted(AppLifeCycleStage level)
    {
        return isAllUpToStageCompleted;
    }

    public virtual bool IsLifeCycleStageCompleted(AppLifeCycleStage level)
    {
        return isInitializationStageCompleted;
    }

    public virtual Task SignalInitializationStageCompletedAsync(AppLifeCycleStage level, object? signaller = null)
    {
        NotifyInitializationStageCompleted(level);
        return Task.CompletedTask;
    }

    public virtual void RegisterRequiredSignaller(AppLifeCycleStage level, object signaller)
    {
    }

    public virtual Task WaitForInitializationStageCompletedAsync(AppLifeCycleStage level, TimeSpan timeout, CancellationToken token = default)
    {
        return Task.CompletedTask;
    }

    protected void NotifyInitializationStageCompleted(AppLifeCycleStage level)
    {
        InitializationStageCompleted?.Invoke(this, new AppInitializationStageCompletedEventArgs(level));
    }

    public virtual void RegisterRequiredExecution(AppLifeCycleStage targetLevel, Func<AppLifeCycleStage, CancellationToken, Task> execution)
    {
        // nop
    }
}
