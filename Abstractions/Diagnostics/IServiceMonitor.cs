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
