using HomeCompanion.Abstractions;

namespace HomeCompanion.Tests.TestUtilities;

internal class StubLifeCycleManager : IHomeCompanionLifeCycleSynchronization
{
    public Task AwaitBusesConnectedAsync(TimeSpan timeout, CancellationToken token = default)
    {
        return Task.CompletedTask;
    }

    public bool IsAllUpToStageCompleted(AppInitializationStage level)
    {
        return true;
    }

    public bool IsInitializationStageCompleted(AppInitializationStage level)
    {
        return true;
    }

    public Task SignalInitializationStageCompletedAsync(AppInitializationStage level)
    {
        return Task.CompletedTask;
    }

    public Task WaitForInitializationStageCompletedAsync(AppInitializationStage level, TimeSpan timeout, CancellationToken token = default)
    {
        return Task.CompletedTask;
    }
}
