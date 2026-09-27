using HomeCompanion.Abstractions;
using HomeCompanion.Extensions;
using HomeCompanion.Persistence;
using HomeCompanion.Values;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Network.OpenHab;
using SRF.Network.OpenHab.Client;
using SRF.Network.OpenHab.Items;
using System.Reflection;
using System.Text.Json;

namespace HomeCompanion.Integrations.OpenHab;

/// <summary>
/// Registers the OpenHAB connector and related services, and performs initial synchronization of values with OpenHAB items on application startup.
/// Value initialization uses OpenHAB-native type conversion and falls back to the generic target value parser.
/// </summary>
public class OpenHabExtensionRegistration(
    ILogger<OpenHabExtensionRegistration> logger
) : IExtensionRegistration
{
    private readonly ILogger<OpenHabExtensionRegistration> logger = logger;

    public void RegisterServices(IExtensionRegistrationContext context)
    {
        // see whether configuration is available, if not, aim to load it
        var configSection = context.Builder.Configuration.GetSection(OpenHabIntegrationOptions.SectionName);
        if (!configSection.Exists())
        {
            logger.LogWarning("OpenHAB configuration section '{SectionName}' not found. OpenHAB integration will be disabled. Please ensure the configuration is present in appsettings.json or environment variables.", OpenHabIntegrationOptions.SectionName);
            return;
        }
        else
        {
            logger.LogInformation("Found OpenHAB configuration section '{SectionName}'. Proceeding with OpenHAB extension registration.", OpenHabIntegrationOptions.SectionName);
        }
        // is it disabled via configuration?
        var integrationOptions = configSection.Get<OpenHabIntegrationOptions>();
        if (integrationOptions is null || !integrationOptions.Enable)
        {
            logger.LogWarning(
                "OpenHAB integration is disabled via configuration (OpenHAB:Enable=false). Skipping registration of connector/provider services. " +
                "To receive OpenHAB events, set OpenHAB:Enable=true in the active runtime profile (for example appsettings.Development.json or external HomeCompanion.json overrides)." );
            return;
        }

        var accessTokenConfigured = !string.IsNullOrWhiteSpace(configSection[nameof(EventBusClientOptions.AccessToken)]);
        logger.LogDebug(
            "Registering OpenHAB integration with section '{SectionName}'. WebSocket='{WebSocket}', RestApi='{RestApi}', SourceEntity='{SourceEntity}', FilterSource={FilterSource}, AccessTokenConfigured={AccessTokenConfigured}.",
            OpenHabIntegrationOptions.SectionName,
            configSection[nameof(EventBusClientOptions.WebSocket)] ?? "<unset>",
            configSection[nameof(EventBusClientOptions.RestApi)] ?? "<unset>",
            configSection[nameof(EventBusClientOptions.SourceEntity)] ?? "<unset>",
            configSection.GetValue<bool?>(nameof(EventBusClientOptions.FilterSource)),
            accessTokenConfigured);

        logger.LogTrace(
            "OpenHAB websocket lifecycle is managed by {HostedService}. {Provider} subscribes to events only and must not call ConnectAsync during startup.",
            nameof(OpenHabConnector),
            nameof(OpenHabConnectivityProvider));

        context.Builder.Services.AddOpenHabConnector();
        context.Builder.Services.AddOptions<OpenHabIntegrationOptions>().BindConfiguration(OpenHabIntegrationOptions.SectionName);
        context.Builder.Services.AddSingleton<OpenHabTypeConversionRegistry>();
        context.Builder.Services.AddSingleton<OpenHabItemMetadataCache>();
        context.Builder.Services.AddSingleton<OpenHabStateConverter>();
        context.Builder.Services.AddSingleton<OpenHabConnectivityProvider>();
        context.Builder.Services.AddSingleton<IConnectivityProvider>(sp => sp.GetRequiredService<OpenHabConnectivityProvider>());
        context.Builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<OpenHabConnectivityProvider>());
        context.Builder.Services.AddHostedService<OpenHabExtensionRegistrationBackgroundService>();
        logger.LogInformation("Registered OpenHAB connectivity extension");
    }
}

/// <summary>
/// Background service responsible for initializing OpenHAB values during the application startup.
/// </summary>
internal class OpenHabExtensionRegistrationBackgroundService : BackgroundService
{
    private readonly IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization;
    private readonly IStateInitializationRegistrar stateInitializationManager;
    private readonly IEnumerable<IValuesContainer> valueContainers;
    private readonly IRestApiClient restApiClient;
    private readonly EventBusClientOptions openHabOptions;
    private readonly OpenHabIntegrationOptions openHabIntegrationOptions;
    private readonly OpenHabItemMetadataCache itemMetadataCache;
    private readonly OpenHabTypeConversionRegistry typeConverter;
    private readonly ILogger<OpenHabExtensionRegistrationBackgroundService> logger;

    public OpenHabExtensionRegistrationBackgroundService(
        IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization,
        IStateInitializationRegistrar stateInitializationManager,
        IEnumerable<IValuesContainer> valueContainers,
        IRestApiClient restApiClient,
        IOptions<EventBusClientOptions> openHabOptions,
        IOptions<OpenHabIntegrationOptions> openHabIntegrationOptions,
        OpenHabItemMetadataCache itemMetadataCache,
        OpenHabTypeConversionRegistry typeConverter,
        ILogger<OpenHabExtensionRegistrationBackgroundService> logger)
    {
        this.lifeCycleSynchronization = lifeCycleSynchronization;
        this.stateInitializationManager = stateInitializationManager;
        this.valueContainers = valueContainers;
        this.restApiClient = restApiClient;
        this.openHabOptions = openHabOptions.Value;
        this.openHabIntegrationOptions = openHabIntegrationOptions.Value;
        this.itemMetadataCache = itemMetadataCache;
        this.typeConverter = typeConverter;
        this.logger = logger;
        stateInitializationManager.RegisterInitialization(AppLifeCycleStage.InitRetrieveFromEnvironment, InitializeValuesFromOpenHabAsync);
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

        itemMetadataCache.Update(itemsByName.Values);

        var initializedValues = new HashSet<IValue>(ReferenceEqualityComparer.Instance);

        int initializedByMapping = 0;
        int initializedByPropertyName = 0;

        foreach (var (propertyName, value) in EnumerateContainerValues())
        {
            bool isByMapping = true;
            if (!value.TryGetBusEndpoint<OpenHabBusEndpointMapping>(OpenHabBusEndpointMapping.BusId, out var mapping) || mapping is null)
            {
                isByMapping = false;
                if (!openHabIntegrationOptions.EnablePropertyNameMatching)
                    continue;
            }

            if (!itemsByName.TryGetValue(mapping?.ItemName ?? propertyName, out var item))
                continue;

            // we have an item and a value that are mapped to each other, now try to initialize the value from the item state using regular OpenHAB type conversion or the generic value parser as fallback
            if (typeConverter.TryConvertValue(item.State, value, item.Type, item, mapping?.Config, out var convertedValue) && convertedValue is not null)
            {
                if (value.InitializeValue(convertedValue, AppLifeCycleStage.InitRetrieveFromEnvironment))
                {
                    initializedValues.Add(value);
                    if (isByMapping)
                        initializedByMapping++;
                    else
                        initializedByPropertyName++;
                }
                else
                {
                    logger.LogDebug("Failed to initialize value '{PropertyName}' from OpenHAB item '{ItemName}'.", propertyName, item.Name);
                }
                continue;
            }

            logger.LogDebug("Failed to convert OpenHAB item state '{State}' of type '{Type}' for value '{PropertyName}' mapped to OpenHAB item '{ItemName}'.", item.State, item.Type, propertyName, item.Name);
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
}
