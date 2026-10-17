namespace HomeCompanion.Diagnostics;

public interface IServiceStatusPeriodic : IServiceStatus
{
    /// <summary>
    /// The resulting service status from the last check. If no check has been performed yet, this will be <see cref="ServiceStatus.Undefined"/>.
    /// </summary>
    [Obsolete("Use GetServiceStatusInfo() instead.")]
    ServiceStatus LastCheckedStatus { get; }

    /// <summary>
    /// The time when the service was last checked. If no check has been performed yet, this will be <see cref="DateTimeOffset.MinValue"/>.
    /// </summary>
    [Obsolete("Use GetServiceStatusInfo() instead.")]
    DateTimeOffset LastCheckedTime { get; }

    /// <summary>
    /// The error message from the last check, if any. If no check has been performed yet or if the last check was successful, this will be <c>null</c>.
    /// This property is optional. It may also be <c>null</c> if the service does not provide error messages.
    /// </summary>
    /// <value>The error message from the last check, or <c>null</c> if none.</value>
    [Obsolete("Use GetServiceStatusInfo() instead.")]
    string[]? LastCheckedErrorMessages { get; }

    /// <summary>
    /// For services that are expected to send periodic life signals, this property indicates the minimum interval between life signals. If no life signal is received within this interval, the service is considered "down".
    /// </summary>
    TimeSpan? MinLifeSignalInterval { get; }

    DateTimeOffset? LastLifeSignalTime { get; }

    /// <summary>
    /// Checks the current status of the service.
    /// </summary>
    /// <returns>The current status of the service.</returns>
    [Obsolete("Use GetServiceStatusInfo() instead.")]
    Task<ServiceStatus> CheckStatusAsync();
}
