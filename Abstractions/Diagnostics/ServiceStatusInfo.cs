namespace HomeCompanion.Diagnostics;

public record ServiceStatusInfo(
    /// <summary>
    /// The name of the service.
    /// </summary>
    string ServiceName,

    /// <summary>
    /// The current status of the service.
    /// </summary>
    ServiceStatus Status,

    /// <summary>
    /// The time when the service was last checked. If no check has been performed yet, this will be <see cref="DateTimeOffset.MinValue"/>.
    /// </summary>
    DateTimeOffset TimeStamp,

    DateTimeOffset? LastUpTime = null,

    Exception[]? Exceptions = null
);
