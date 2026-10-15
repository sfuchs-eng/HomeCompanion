namespace HomeCompanion.Diagnostics;

/// <summary>
/// Represents the possible statuses of a service from the perspective of the service monitor implementing <see cref="IServiceMonitor"/>.
/// A service can be "Up", "Down", or "Undefined" if its status has not been checked yet.
/// </summary>
public enum ServiceStatus
{
    Undefined,

    /// <summary>
    /// The service is operational and responding as expected.
    /// </summary>
    Up,

    /// <summary>
    /// The service is not operational or not responding as expected.
    /// </summary>
    Down,

    /// <summary>
    /// The service is operational, may respond incorrectly or inconsistently, indicating a potential issue that may require attention or investigation.
    /// </summary>
    Deviation
}
