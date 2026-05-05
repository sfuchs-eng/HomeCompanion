namespace HomeCompanion.Core;

/// <summary>
/// Configuration options for the HomeCompanion core services.
/// </summary>
public class CoreOptions
{
    /// <summary>
    /// Maximum time to wait for all <see cref="HomeCompanion.IConnectivityProvider"/> instances
    /// to report both <c>IsConnected</c> and <c>IsInitializationFinished</c> before logic initialization
    /// proceeds anyway.
    /// </summary>
    /// <remarks>
    /// Defaults to 5 minutes. Set to <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to wait
    /// indefinitely (not recommended for production).
    /// </remarks>
    public TimeSpan BusInitializationTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Optional path to a directory from which additional extension assemblies are loaded at startup.
    /// All <c>*.dll</c> files found directly in this directory are loaded before the service discovery scan runs.
    /// When <see langword="null" /> or empty, only assemblies already present in <see cref="AppContext.BaseDirectory"/> are used.
    /// </summary>
    /// <remarks>
    /// Supports a plugin/drop-in model: place any <c>HomeCompanion</c>-compatible assembly in this directory
    /// and it will be discovered automatically without modifying the Server project references.
    /// </remarks>
    public string? ExtensionsPath { get; set; }
}
