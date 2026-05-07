namespace HomeCompanion.Persistence;

public enum StateInitializationStage
{
    /// <summary>
    /// The default value from object construction, no specific initialization.
    /// </summary>
    Default = 0,

    /// <summary>
    /// Initial values are loaded from a persistent store, e.g. from JSON files via <see cref="JsonFilesStateStore"/>.
    /// </summary>
    InitLoadFromStore = 1,

    /// <summary>
    /// Initial values are retrieved from the environment, e.g. from OpenHAB via <see cref="OpenHabConnector"/>.
    /// </summary>
    InitRetrieveFromEnvironment = 2,

    /// <summary>
    /// A value has been received from the bus, e.g. from OpenHAB via <see cref="OpenHabConnector"/>.
    /// </summary>
    InitBusValueReceived = 3,

    /// <summary>
    /// Values are saved during shutdown, e.g. to a persistent store.
    /// </summary>
    ShutDownSave = 20
}