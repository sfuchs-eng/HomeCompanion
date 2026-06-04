namespace HomeCompanion.Server.Quartz;

/// <summary>
/// Configures optional Quartz persistent store hosted by HomeCompanion.Server.
/// </summary>
public sealed class QuartzFileStoreOptions
{
    /// <summary>
    /// Enables Quartz SQLite file-store backed persistence.
    /// If false, Quartz runs with default/in-memory store unless overridden by Quartz section properties.
    /// </summary>
    public bool EnableFileStore { get; set; }

    /// <summary>
    /// SQLite file path for Quartz job store.
    /// Relative paths are resolved against the application base directory.
    /// </summary>
    public string StoreFilePath { get; set; } = "state/quartz/quartz-store.db";

    /// <summary>
    /// Quartz scheduler name.
    /// </summary>
    public string SchedulerName { get; set; } = "HomeCompanionScheduler";

    /// <summary>
    /// Quartz scheduler instance id.
    /// Use AUTO to let Quartz generate one.
    /// </summary>
    public string SchedulerId { get; set; } = "AUTO";

    /// <summary>
    /// Enables Quartz schema validation at startup.
    /// Keep enabled when schema is managed externally.
    /// </summary>
    public bool PerformSchemaValidation { get; set; } = true;

    /// <summary>
    /// Wait for running jobs on graceful shutdown.
    /// </summary>
    public bool WaitForJobsToComplete { get; set; } = true;
}
