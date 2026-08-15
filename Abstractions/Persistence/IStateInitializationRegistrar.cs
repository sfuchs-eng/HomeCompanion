using HomeCompanion.Abstractions;

namespace HomeCompanion.Persistence;

/// <summary>
/// Allows components to register additional initialization and save hooks with a state initialization manager.
/// </summary>
public interface IStateInitializationRegistrar
{
    void RegisterInitialization(AppLifeCycleStage stage, StateInitializationDelegate initialization);
    void RemoveInitialization(AppLifeCycleStage stage, StateInitializationDelegate initialization);
    void RegisterSave(StateInitializationDelegate save);
    void RemoveSave(StateInitializationDelegate save);
}

public delegate Task StateInitializationDelegate(CancellationToken token = default);
