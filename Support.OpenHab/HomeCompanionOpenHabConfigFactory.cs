using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Knx.Config;

namespace HomeCompanion.Support.OpenHab;

public class OpenHabItemValueConfiguration
{
    public string OpenHabItemName { get; set; } = string.Empty;

    public Type ValueType { get; set; } = typeof(object);
}

public sealed class HomeCompanionOpenHabConfigFactory(
    IOptions<KnxSystemConfigOptions> options, // Values container namespace, OpenHAB IValues container file path, etc. is all in there for now. Consider untangling this in the future.
    OpenHabValuesCodeGenerator openHabValuesCodeGenerator,
    ILogger<HomeCompanionOpenHabConfigFactory> logger
) : IHomeCompanionOpenHabConfigFactory
{
    private readonly KnxSystemConfigOptions options = options.Value;
    private readonly OpenHabValuesCodeGenerator openHabValuesCodeGenerator = openHabValuesCodeGenerator;
    private readonly ILogger<HomeCompanionOpenHabConfigFactory> logger = logger;

    public Task GenerateOpenHabItemsValueContainerAsync(CancellationToken cancellationToken = default)
    {
        // pull existing code from SRF.Network.Cli and improve to support UnitsNet based values

        // use the OpenHabValuesCodeGenerator to generate the code for the OpenHAB items value container
        throw new NotImplementedException();
    }
}
