using HomeCompanion.Integrations.OpenHab;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Network.OpenHab;

namespace HomeCompanion.Support.OpenHab;

public class OpenHabValuesCodeGenOptions
{
    public static string ConfigSectionName => "OpenHab:CodeGen";
    public bool IgnoreItemsWithKnxMapping { get; set; } = true;
}

/// <summary>
/// Depends on the OpenHAB integration being registered in the DI container, and generates C# code for HomeCompanion values based on a live OpenHAB instance.
/// Use <see cref="SRF.Network.OpenHab.OpenHabHostingHelpers.AddOpenHabConnector(Microsoft.Extensions.Hosting.IHostBuilder, string)"/> to register the OpenHAB integration in the DI container before using this code generator.
/// Items having a KNX mapping will be ignored by default, as they are already handled by the KNX configuration code generation.
/// </summary>
public class OpenHabValuesCodeGenerator(
    IOptions<OpenHabValuesCodeGenOptions> options,
    OpenHabTypeConversionRegistry typeConversionRegistry,
    // need the Rest API client to fetch the list of items from OpenHAB
    IRestApiClient restApiClient,
    ILogger<OpenHabValuesCodeGenerator> logger
)
{
    private readonly OpenHabValuesCodeGenOptions options = options.Value;
    private readonly OpenHabTypeConversionRegistry typeConversionRegistry = typeConversionRegistry;
    private readonly IRestApiClient restApiClient = restApiClient;
    private readonly ILogger<OpenHabValuesCodeGenerator> logger = logger;
    
    public async Task GenerateAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
        /*
        logger.LogInformation("Starting C# code generation for HomeCompanion values based on OpenHAB items...");

        var items = await restApiClient.GetItemsAsync(cancellationToken);

        // filter out items that have a KNX mapping if the option is set
        if (options.IgnoreItemsWithKnxMapping)
        {
            items = items.Where(item => !typeConversionRegistry.HasKnxMapping(item)).ToList();
        }

        // generate C# code for each item
        foreach (var item in items)
        {
            var code = typeConversionRegistry.GenerateValueCode(item);
            var filePath = Path.Combine(outputDirectory, $"{item.Name}.cs");
            await File.WriteAllTextAsync(filePath, code, cancellationToken);
            logger.LogInformation("Generated C# code for item {ItemName} at {FilePath}", item.Name, filePath);
        }

        logger.LogInformation("C# code generation completed successfully.");
        */
    }
}
