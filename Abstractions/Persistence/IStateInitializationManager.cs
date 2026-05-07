namespace HomeCompanion.Persistence;

/// <summary>
/// Coordinates and executes the initialization of values, e.g. by loading from a persistent store and/or by retrieving initial values from the environment, e.g. from OpenHAB via <see cref="OpenHabConnector"/>.
/// 
/// The <see cref="IStateInitializationManager"/> is to be a singleton service invoking <see cref="IValue.InitializeValue(object, HomeCompanion.Persistence.StateInitializationStage)"/> of the registered values.
/// The initialization is executed in stages defined by <see cref="StateInitializationStage"/> to allow for a flexible and extensible initialization process.
/// </summary>
public interface IStateInitializationManager
{
    /// <summary>
    /// Initializes the values, iterating through the stages defined by <see cref="StateInitializationStage"/>.
    /// The initialization service needs to directly initialize the values in the respective stages by calling their
    /// <see cref="IValue.InitializeValue(object, StateInitializationStage)"/> method, which will execute the value-specific initialization logic implemented in the concrete value class.
    /// </summary>
    Task InitializeStateAsync(CancellationToken token = default);

    /// <summary>
    /// Saves the values, e.g. to a persistent store, to persist changes to the values across restarts of the application.
    /// </summary>
    /// <param name="token">A cancellation token to cancel the save operation.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    Task SaveStateAsync(CancellationToken token = default);

    /// <summary>
    /// Represents the reached stage of initialization, e.g. for consumption in <see cref="ILogic"/> implementations for conditional execution of logic depending on the initialization stage.
    /// </summary>
    StateInitializationStage CurrentStage { get; }

    void RegisterInitialization(StateInitializationStage stage, StateInitializationDelegate initialization);
    void RemoveInitialization(StateInitializationStage stage, StateInitializationDelegate initialization);
    void RegisterSave(StateInitializationDelegate save);
    void RemoveSave(StateInitializationDelegate save);
}

public delegate Task StateInitializationDelegate(CancellationToken token = default);
