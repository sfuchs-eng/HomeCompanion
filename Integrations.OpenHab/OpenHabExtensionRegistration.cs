using HomeCompanion.Abstractions;
using HomeCompanion.Extensions;
using HomeCompanion.Persistence;
using HomeCompanion.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Knx.Config;
using SRF.Network.OpenHab;
using SRF.Network.OpenHab.Client;
using SRF.Network.OpenHab.Items;
using System.Reflection;
using System.Text.Json;

namespace HomeCompanion.Integrations.OpenHab;

public class OpenHabExtensionRegistration(
    ILogger<OpenHabExtensionRegistration> logger
) : IExtensionRegistration
{
    private readonly ILogger<OpenHabExtensionRegistration> logger = logger;

    public void RegisterServices(IExtensionRegistrationContext context)
    {
        context.Builder.Services.AddOpenHabConnector();
        context.Builder.Services.AddOptions<OpenHabIntegrationOptions>().BindConfiguration(OpenHabIntegrationOptions.SectionName);
        context.Builder.Services.AddSingleton<OpenHabStateConverter>();
        context.Builder.Services.AddHostedService<OpenHabExtensionRegistrationBackgroundService>();
        logger.LogInformation("Registered OpenHAB connectivity extension");
    }
}

internal class OpenHabExtensionRegistrationBackgroundService : BackgroundService
{
    private readonly IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization;
    private readonly IStateInitializationManager stateInitializationManager;
    private readonly IEnumerable<IValuesContainer> valueContainers;
    private readonly IRestApiClient restApiClient;
    private readonly EventBusClientOptions openHabOptions;
    private readonly OpenHabIntegrationOptions openHabIntegrationOptions;
    private readonly KnxConfiguration knxConfig;
    private readonly OpenHabStateConverter stateConverter;
    private readonly ILogger<OpenHabExtensionRegistrationBackgroundService> logger;

    public OpenHabExtensionRegistrationBackgroundService(
        IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization,
        IStateInitializationManager stateInitializationManager,
        IEnumerable<IValuesContainer> valueContainers,
        IRestApiClient restApiClient,
        IOptions<EventBusClientOptions> openHabOptions,
        IOptions<OpenHabIntegrationOptions> openHabIntegrationOptions,
        IOptions<KnxConfiguration> knxConfig,
        OpenHabStateConverter stateConverter,
        ILogger<OpenHabExtensionRegistrationBackgroundService> logger)
    {
        this.lifeCycleSynchronization = lifeCycleSynchronization;
        this.stateInitializationManager = stateInitializationManager;
        this.valueContainers = valueContainers;
        this.restApiClient = restApiClient;
        this.openHabOptions = openHabOptions.Value;
        this.openHabIntegrationOptions = openHabIntegrationOptions.Value;
        this.knxConfig = knxConfig.Value;
        this.stateConverter = stateConverter;
        this.logger = logger;
        stateInitializationManager.RegisterInitialization(AppInitializationStage.InitRetrieveFromEnvironment, InitializeValuesFromOpenHabAsync);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Retrieve all current item values from OpenHAB and see which ones can be mapped to registered values in the application.
    /// Mapping is done via <see cref="IValue.BusMappings"/> or if the property name matches the OpenHAB item name.
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    private async Task InitializeValuesFromOpenHabAsync(CancellationToken token)
    {
        if (!openHabOptions.Enable)
        {
            logger.LogInformation("Skipping OpenHAB initialization because OpenHAB connector is disabled.");
            return;
        }

        logger.LogInformation("Starting OpenHAB initialization: fetching items and initializing values from OpenHAB state.");

        Item[] items;
        try
        {
            items = await restApiClient.GetItemsAsync(token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch OpenHAB items for value initialization.");
            return;
        }

        var itemsByName = items
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var stateMap = LoadStateMap();
        var initializedValues = new HashSet<IValue>(ReferenceEqualityComparer.Instance);

        int initializedByMapping = 0;
        int initializedByPropertyName = 0;

        foreach (var (propertyName, value) in EnumerateContainerValues())
        {
            if (!value.TryGetBusEndpoint<OpenHabBusEndpointMapping>(OpenHabBusEndpointMapping.BusId, out var mapping) || mapping is null)
                continue;

            if (!itemsByName.TryGetValue(mapping.ItemName, out var item))
                continue;

            if (!TryGetPreparedState(item, stateMap, out var preparedState))
                continue;

            // Try DPT-aware conversion first if value has KNX mapping
            if (stateConverter.TryConvertValue(item.State, value, out var convertedValue) && convertedValue is not null)
            {
                if (value.InitializeValue(convertedValue, AppInitializationStage.InitRetrieveFromEnvironment))
                {
                    initializedValues.Add(value);
                    initializedByMapping++;
                }
                else
                {
                    logger.LogDebug("Failed to initialize value '{PropertyName}' from OpenHAB item '{ItemName}'.", propertyName, item.Name);
                }
            }
            else if (value.InitializeValue(preparedState, AppInitializationStage.InitRetrieveFromEnvironment))
            {
                initializedValues.Add(value);
                initializedByMapping++;
            }
            else
            {
                logger.LogDebug("Failed to initialize value '{PropertyName}' from OpenHAB item '{ItemName}'.", propertyName, item.Name);
            }
        }

        if (openHabIntegrationOptions.EnablePropertyNameMatching)
        {
            foreach (var (propertyName, value) in EnumerateContainerValues())
            {
                if (initializedValues.Contains(value))
                    continue;

                if (!itemsByName.TryGetValue(propertyName, out var item))
                    continue;

                if (!TryGetPreparedState(item, stateMap, out var preparedState))
                    continue;

                // Try DPT-aware conversion first if value has KNX mapping
                if (stateConverter.TryConvertValue(item.State, value, out var convertedValue) && convertedValue is not null)
                {
                    if (value.InitializeValue(convertedValue, AppInitializationStage.InitRetrieveFromEnvironment))
                    {
                        initializedByPropertyName++;
                    }
                    else
                    {
                        logger.LogDebug("Failed to initialize value '{PropertyName}' from property-name matched OpenHAB item '{ItemName}'.", propertyName, item.Name);
                    }
                }
                else if (value.InitializeValue(preparedState, AppInitializationStage.InitRetrieveFromEnvironment))
                {
                    initializedByPropertyName++;
                }
                else
                {
                    logger.LogDebug("Failed to initialize value '{PropertyName}' from property-name matched OpenHAB item '{ItemName}'.", propertyName, item.Name);
                }
            }
        }

        logger.LogInformation(
            "OpenHAB initialization finished. Retrieved {ItemCount} items. Initialized {ByMapping} values via bus mapping and {ByPropertyName} values via property-name matching.",
            itemsByName.Count,
            initializedByMapping,
            initializedByPropertyName);
    }

    private IEnumerable<(string PropertyName, IValue Value)> EnumerateContainerValues()
    {
        foreach (var container in valueContainers)
        {
            var properties = container.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => typeof(IValue).IsAssignableFrom(p.PropertyType) && p.CanRead);

            foreach (var property in properties)
            {
                if (property.GetValue(container) is IValue value)
                    yield return (property.Name, value);
            }
        }
    }

    private Dictionary<string, string> LoadStateMap()
    {
        var stateMapPath = Path.Combine(knxConfig.OpenHab.TemplatesFolder, openHabIntegrationOptions.StateMapFile);
        if (!File.Exists(stateMapPath))
        {
            logger.LogDebug("No OpenHAB state map file found at '{StateMapPath}'. Continuing without custom state mapping.", stateMapPath);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var content = File.ReadAllText(stateMapPath);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load OpenHAB state map from '{StateMapPath}'. Continuing without custom state mapping.", stateMapPath);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private bool TryGetPreparedState(Item item, Dictionary<string, string> stateMap, out object preparedState)
    {
        preparedState = item.State;
        if (string.Equals(item.State, "NULL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.State, "UNDEF", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (stateMap.TryGetValue(item.State, out var mappedState))
            preparedState = mappedState;

        return true;
    }
}
