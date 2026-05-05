namespace HomeCompanion.Values;

/// <summary>
/// Coordinates and executes the initialization of values, e.g. by loading from a persistent store and/or by retrieving initial values from the environment, e.g. from OpenHAB via <see cref="OpenHabConnector"/>.
/// 
/// The <see cref="IValuesInitializationManager"/> is to be a singleton service raising <see cref="IValueInitializationEvent"/> events which are handled by the individual value implementations, e.g. <see cref="OpenHabValue"/>.
/// The initialization is executed in stages defined by <see cref="ValuesInitializationStage"/> to allow for a flexible and extensible initialization process.
/// </summary>
public interface IValuesInitializationManager
{
    /// <summary>
    /// Initializes the values, iterating through the stages defined by <see cref="ValuesInitializationStage"/>.
    /// The initialization serice needs to directly initialize the values in the respective stages by calling their
    /// <see cref="IValue.InitializeValue(object, ValuesInitializationStage)"/> method, which will execute the value-specific initialization logic implemented in the concrete value class.
    /// </summary>
    Task InitializeValuesAsync(CancellationToken token = default);

    /// <summary>
    /// Represents the reached stage of initialization, e.g. for consumption in <see cref="ILogic"/> implementations for conditional execution of logic depending on the initialization stage.
    /// </summary>
    ValuesInitializationStage CurrentStage { get; }
}
