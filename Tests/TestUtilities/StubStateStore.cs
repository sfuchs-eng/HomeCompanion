using HomeCompanion.Persistence;

namespace HomeCompanion.Tests.TestUtilities;

internal sealed class StubStateStore() : IStateStore
{
    public Task<StateLoadingResult<T>> LoadAsync<T>(string stateSetName, TimeSpan maxAge) where T : class, new()
    {
        T state;
        /*
        if (typeof(T) == typeof(ShutterManualOverrideStateSet) && _preloadedState is not null)
            state = (T)(object)_preloadedState;
        else
            state = new T();
            */
        state = new T();

        return Task.FromResult(new StateLoadingResult<T>
        {
            IsSuccess = true,
            IsRecent = true,
            StateSet = state,
        });
    }

    public Task<StateLoadingResult<T>> LoadAsync<T>(string stateSetName) where T : class, new()
        => LoadAsync<T>(stateSetName, TimeSpan.FromMinutes(30));

    public Task SaveAsync<T>(string stateSetName, T stateSet, CancellationToken cancellation) where T : class, new()
    {
        /*
        if (stateSet is ShutterManualOverrideStateSet typed)
            Stored = typed;
        */
        return Task.CompletedTask;
    }

    public Task SaveAsync<T>(string stateSetName, T stateSet, int timeoutSeconds = 30) where T : class, new()
    {
        /*
        if (stateSet is ShutterManualOverrideStateSet typed)
            Stored = typed;
        */
        return Task.CompletedTask;
    }
}
