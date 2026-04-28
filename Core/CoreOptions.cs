namespace HomeCompanion.Core;

/// <summary>
/// Configuration options for the HomeCompanion core services.
/// </summary>
public class CoreOptions
{
    /// <summary>
    /// Maximum time to wait for all <see cref="HomeCompanion.Abstractions.IConnectivityProvider"/> instances
    /// to report both <c>IsConnected</c> and <c>IsInitializationFinished</c> before logic initialization
    /// proceeds anyway.
    /// </summary>
    /// <remarks>
    /// Defaults to 5 minutes. Set to <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to wait
    /// indefinitely (not recommended for production).
    /// </remarks>
    public TimeSpan BusInitializationTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
