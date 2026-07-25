namespace HomeCompanion.Diagnostics;

/// <summary>
/// Entity that monitors the status of services and provides diagnostic information about them.
/// The entity is responsible for checking the status of services, e.g. periodically as required by each service, and reporting their health status.
/// </summary>
/// <remarks>
/// Typically implemented by a local logic that monitors the status of services and provides diagnostic information about them.
/// </remarks>
public interface IServiceMonitor
{
    IEnumerable<IStatefulService> Services { get; }
}

public interface IStatefulService
{
    /// <summary>
    /// The name of the service.
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// The resulting service status from the last check. If no check has been performed yet, this will be <see cref="ServiceStatus.Undefined"/>.
    /// </summary>
    ServiceStatus LastCheckedStatus { get; }

    /// <summary>
    /// The time when the service was last checked. If no check has been performed yet, this will be <see cref="DateTimeOffset.MinValue"/>.
    /// </summary>
    DateTimeOffset LastCheckedTime { get; }

    /// <summary>
    /// The error message from the last check, if any. If no check has been performed yet or if the last check was successful, this will be <c>null</c>.
    /// This property is optional. It may also be <c>null</c> if the service does not provide error messages.
    /// </summary>
    /// <value>The error message from the last check, or <c>null</c> if none.</value>
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
    Task<ServiceStatus> CheckStatusAsync();
}

/// <summary>
/// Represents the possible statuses of a service from the perspective of the service monitor implementing <see cref="IServiceMonitor"/>.
/// A service can be "Up", "Down", or "Undefined" if its status has not been checked yet.
/// </summary>
public enum ServiceStatus
{
    Undefined,
    Up,
    Down
}
