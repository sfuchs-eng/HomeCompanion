namespace HomeCompanion.Abstractions;

public enum AppLifeCycleStage
{
    /// <summary>
    /// The application is being built, but the final service provider is not yet available.
    /// Any object instantiated at this stage will not have its dependencies injected, so this stage is only relevant for static initialization or similar.
    /// Injection services from a temporary service provider passed to extension registrations can be used, but should be used with caution as they might not have the same configuration as the final service provider.
    /// E.g. configuration options and master data, e.g. KNX configuration, can be used from the temporary service provider at this stage but only temporarily and without caching.
    /// </summary>
    PreBuild,

    /// <summary>
    /// The application is built, the final service provider is available, but the application hasn't started running yet.
    /// </summary>
    PreRun,

    /// <summary>
    /// The default value from object construction, no specific initialization.
    /// App running, regular services available, but no specific initialization stage reached yet.
    /// </summary>
    Default,

    /// <summary>
    /// All discovered <see cref="IValue"/> instances are initialized and registered in <see cref="IValuesManager"/>.
    /// Connectivity providers can safely process and publish inbound bus value events after this stage is completed.
    /// </summary>
    InitValuesRegistered,

    /// <summary>
    /// Initial values are loaded from a persistent store, e.g. from JSON files via <see cref="JsonFilesStateStore"/>.
    /// </summary>
    InitLoadFromStore,

    /// <summary>
    /// Initial values are retrieved from the environment, e.g. from OpenHAB via <see cref="OpenHabConnector"/>.
    /// </summary>
    InitRetrieveFromEnvironment,

    /// <summary>
    /// The runtime model has been created from configuration and is ready for consumption by logic modules.
    /// </summary>
    InitModelReady,

    /// <summary>
    /// Logic modules are initialized and ready for operation.
    /// </summary>
    InitLogics,

    /// <summary>
    /// A value has been received from the bus, e.g. from OpenHAB via <see cref="OpenHabConnector"/>.
    /// </summary>
    InitBusValueReceived,

    /// <summary>
    /// <see cref="ILogic"/> components are terminated during shutdown.
    /// </summary>
    TerminateLogics,

    /// <summary>
    /// Values are saved during shutdown, e.g. to a persistent store.
    /// </summary>
    ShutDownSave,

    /// <summary>
    /// Backend services shutdown phase, e.g. InfluxDB, OpenHAB connector, etc. are being shut down.
    /// </summary>
    BackendShutdown,

    /// <summary>
    /// The application has completed its shutdown sequence.
    /// </summary>
    ShutDownCompleted
}

public static class AppInitializationStageExtensions
{
    public static bool IsBefore(this AppLifeCycleStage stage, AppLifeCycleStage otherStage)
    {
        return stage < otherStage;
    }

    public static bool IsAfter(this AppLifeCycleStage stage, AppLifeCycleStage otherStage)
    {
        return stage > otherStage;
    }

    public static bool IsPreRunStage(this AppLifeCycleStage stage)
    {
        return stage == AppLifeCycleStage.PreBuild || stage == AppLifeCycleStage.PreRun;
    }

    public static bool IsInitializationStage(this AppLifeCycleStage stage)
    {
        return stage == AppLifeCycleStage.InitValuesRegistered ||
               stage == AppLifeCycleStage.InitLoadFromStore ||
               stage == AppLifeCycleStage.InitRetrieveFromEnvironment ||
               stage == AppLifeCycleStage.InitModelReady ||
               stage == AppLifeCycleStage.InitBusValueReceived;
    }

    public static bool IsTerminationStage(this AppLifeCycleStage stage)
    {
        return stage >= FirstTerminationStage(stage);
    }

    public static AppLifeCycleStage FirstTerminationStage(this AppLifeCycleStage stage)
    {
        return AppLifeCycleStage.TerminateLogics;
    }

    public static bool IsLastStage(this AppLifeCycleStage stage)
    {
        return stage == AppLifeCycleStage.ShutDownCompleted;
    }
}