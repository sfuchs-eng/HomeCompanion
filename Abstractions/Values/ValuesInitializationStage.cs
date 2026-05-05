namespace HomeCompanion.Values;

public enum ValuesInitializationStage
{
    /// <summary>
    /// The default value from object construction, no specific initialization.
    /// </summary>
    Default = 0,

    /// <summary>
    /// Initial values are loaded from a persistent store, e.g. from JSON files via <see cref="JsonFilesStateStore"/>.
    /// </summary>
    LoadFromStore = 1,

    /// <summary>
    /// Initial values are retrieved from the environment, e.g. from OpenHAB via <see cref="OpenHabConnector"/>.
    /// </summary>
    RetrieveFromEnvironment = 2,

    /// <summary>
    /// A value has been received from the bus, e.g. from OpenHAB via <see cref="OpenHabConnector"/>.
    /// </summary>
    BusValueReceived = 3
}