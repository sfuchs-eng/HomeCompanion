using HomeCompanion.Values;

namespace HomeCompanion.Core;

/// <summary>
/// Configuration options for the HomeCompanion core services.
/// </summary>
public class CoreOptions
{
    /// <summary>
    /// Optional single directory containing additional HomeCompanion JSON configuration files.
    /// All top-level <c>*.json</c> files are loaded in alphabetical order.
    /// Relative paths are resolved against the host content root.
    /// </summary>
    public string? ConfigDirectory { get; set; }

    /// <summary>
    /// Optional list of additional directories containing HomeCompanion JSON configuration files.
    /// All top-level <c>*.json</c> files are loaded in alphabetical order for each directory.
    /// Relative paths are resolved against the host content root.
    /// </summary>
    public List<string> ConfigDirectories { get; set; } = [];

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

    /// <summary>
    /// Optional logic property bindings that override <see cref="ValueBindingAttribute"/> declarations.
    /// </summary>
    /// <remarks>
    /// Keys must use one of the following formats:
    /// <list type="bullet">
    /// <item><c>FullLogicTypeName.PropertyName</c></item>
    /// <item><c>LogicTypeName.PropertyName</c></item>
    /// </list>
    /// Values are parsed using <see cref="IValueReferenceProvider"/> reference formats.
    /// </remarks>
    public Dictionary<string, string> LogicValueBindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
