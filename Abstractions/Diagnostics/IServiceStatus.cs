namespace HomeCompanion.Diagnostics;

public interface IServiceStatus
{
    /// <summary>
    /// The name of the service.
    /// </summary>
    string ServiceName { get; }

    object? Service { get; }

    Task<ServiceStatusInfo> GetServiceStatusInfoAsync();
}
