namespace HomeCompanion.Persistence;

/// <summary>
/// Interface for a state store that can load and save state objects.
/// Typically used for persisting <see cref="IValue"/> and <see cref="ILogic"/> state across restarts.
/// </summary>
public interface IStateStore
{
    Task<StateLoadingResult<T>> LoadAsync<T>(string stateSetName, TimeSpan maxAge) where T : class, new();
    Task<StateLoadingResult<T>> LoadAsync<T>(string stateSetName) where T : class, new();
    Task SaveAsync<T>(string stateSetName, T stateSet, CancellationToken cancellation) where T : class, new();
    Task SaveAsync<T>(string stateSetName, T stateSet, int timeoutSeconds = 30) where T : class, new();
}

public class StateLoadingResult<T> where T : class, new()
{
    public bool IsSuccess { get; set; }
    public bool IsRecent { get; set; }
    public T StateSet { get; set; } = new T();
}
