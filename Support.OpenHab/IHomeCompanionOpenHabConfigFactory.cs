namespace HomeCompanion.Support.OpenHab;

/// <summary>
/// Determines the OpenHAB configuration for HomeCompanion based on a runtime OpenHAB instance and OpenHAB as well as KNX specific configuration files.<br/>
/// Generates the HomeCompanion IValueContainer reflecting OpenHAB Items and their corresponding value types.
/// </summary>
public interface IHomeCompanionOpenHabConfigFactory
{
    /// <summary>
    /// Generates the OpenHAB configuration files for HomeCompanion based on the provided KNX configuration.
    /// </summary>
    /// <param name="knxConfig">The KNX configuration to generate the OpenHAB configuration from.</param>
    /// <param name="outputDirectory">The directory where the generated OpenHAB configuration files will be saved.</param>
    Task GenerateOpenHabItemsValueContainerAsync(CancellationToken cancellationToken = default);
}
