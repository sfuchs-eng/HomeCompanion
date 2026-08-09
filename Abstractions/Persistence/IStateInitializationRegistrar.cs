using HomeCompanion.Abstractions;

namespace HomeCompanion.Persistence;

/// <summary>
/// Allows components to register additional initialization and save hooks with a state initialization manager.
/// </summary>
public interface IStateInitializationRegistrar
{
    void RegisterInitialization(AppInitializationStage stage, StateInitializationDelegate initialization);
    void RemoveInitialization(AppInitializationStage stage, StateInitializationDelegate initialization);
    void RegisterSave(StateInitializationDelegate save);
    void RemoveSave(StateInitializationDelegate save);
}

public delegate Task StateInitializationDelegate(CancellationToken token = default);
