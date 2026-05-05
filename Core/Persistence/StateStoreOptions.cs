
namespace HomeCompanion.Persistence;

public class StateStoreOptions
{
    /// <summary>
    /// Directory, absolute or relative to the application base directory, where state files are stored.
    /// </summary>
    /// <value></value>
    public string Directory { get; set; } = "States";
}
