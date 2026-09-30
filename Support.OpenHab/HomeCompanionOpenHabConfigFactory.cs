using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Knx.Config;
using SRF.Knx.Config.OpenHab;
using SRF.Network.OpenHab;

namespace HomeCompanion.Support.OpenHab;

public class OpenHabItemValueConfiguration
{
    public string OpenHabItemName { get; set; } = string.Empty;

    public Type? ValueType { get; set; }
}

public sealed class HomeCompanionOpenHabConfigFactory(
    IOptions<KnxSystemConfigOptions> options, // Values container namespace, OpenHAB IValues container file path, etc. is all in there for now. Consider untangling this in the future.
    OpenHabValuesCodeGenerator openHabValuesCodeGenerator,
    IRestApiClient openhabRestApiClient,
    //IEventBusClient eventBusClient,
    IServiceProvider serviceProvider,
    ILogger<HomeCompanionOpenHabConfigFactory> logger
) : IHomeCompanionOpenHabConfigFactory
{
    private readonly KnxSystemConfigOptions config = options.Value;
    private readonly OpenHabValuesCodeGenerator openHabValuesCodeGenerator = openHabValuesCodeGenerator;
    private readonly ILogger<HomeCompanionOpenHabConfigFactory> logger = logger;

    public async Task GenerateOpenHabItemsValueContainerAsync(CancellationToken stoppingToken = default)
    {
        // get KNX configuration from the service provider and config files, not from OpenHAB (would be better, might be something for the future)
        var knxConfig = serviceProvider.GetRequiredService<IKnxConfigFactory>().GetDomainConfig();
        var openHabKnxConfig = serviceProvider.GetRequiredService<IOpenHabKnxConfigFactory>()
            .Get(knxConfig);
        var openHabKnxItemsNames = openHabKnxConfig.Things
            .SelectMany(t => t.GroupAddresses)
            .Select(c => c.Item?.Name ?? c.Name)
            .Distinct()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // get all OpenHAB items from the OpenHAB REST API
        var allItems = await openhabRestApiClient.GetItemsAsync(stoppingToken);

        // items from OpenHAB that are not mapped to KNX group addresses
        var ignoreKnxMappedItems = this.config.HomeCompanion.IgnoreOpenHabItemsWithKnxMapping;
        var nonKnxItems = allItems
            .Where(i => !ignoreKnxMappedItems || !openHabKnxItemsNames.Contains(i.Name))
            .Select(i => new OpenHabItemInfo { Name = i.Name, Type = i.Type, State = i.State })
            .ToList();

        // get config
        var config = this.config; //serviceProvider.GetRequiredService<IOptions<KnxSystemConfigOptions>>().Value;
        /*
        if (config is null)
        {
            logger.LogError("KNX system configuration is not available. Please check your configuration.");
            return;
        }
        */

        var filePath = config.HomeCompanion.OpenHabValuesCodeGenFilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            logger.LogError("OpenHAB values code generation file path is not specified in the KNX system configuration. Please check your configuration.");
            return;
        }

        var nameSpace = config.HomeCompanion.GeneratedValuesClassesNamespace;
        if (string.IsNullOrWhiteSpace(nameSpace))
        {
            logger.LogError("OpenHAB values code generation namespace is not specified in the KNX system configuration. Please check your configuration.");
            return;
        }

        var className = config.HomeCompanion.OpenHabValuesClassName;
        if (string.IsNullOrWhiteSpace(className))
        {
            logger.LogError("OpenHAB values code generation class name is not specified in the KNX system configuration. Please check your configuration.");
            return;
        }

        var code = openHabValuesCodeGenerator.Generate(nonKnxItems, className, nameSpace);

        File.WriteAllText(filePath, code, System.Text.Encoding.UTF8);
        logger.LogInformation("Generated OpenHabValues source with {count} IValues for OpenHAB items and wrote to '{file}'",
            nonKnxItems.Count,
            filePath);
    }
}
