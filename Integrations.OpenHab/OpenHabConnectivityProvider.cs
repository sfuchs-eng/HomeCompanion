using HomeCompanion.Events;
using HomeCompanion.Integrations.OpenHab.Events;
using HomeCompanion.Abstractions;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using SRF.Network.OpenHab;
using SRF.Network.OpenHab.Client;
using SRF.Network.OpenHab.EventBus.Events;
using SRF.Network.OpenHab.Items;
using Microsoft.Extensions.Options;
using System.Threading;

namespace HomeCompanion.Integrations.OpenHab;

/// <summary>
/// OpenHab connectivity provider. Bridges the OpenHab event bus to the HomeCompanion event bus.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inbound</b> (OpenHab → EventBus): Subscribes to <see cref="IEventBusClient.EventReceived"/>.
/// <see cref="ItemEventTypeValue"/> with <see cref="SRF.Network.OpenHab.EventBus.EventType.ItemStateEvent"/> is converted to <see cref="OpenHabItemState"/>,
/// <see cref="ItemStateChangedEvent"/> is converted to <see cref="OpenHabItemStateChanged"/>, and
/// <see cref="ItemEventTypeValue"/> with <see cref="SRF.Network.OpenHab.EventBus.EventType.ItemCommandEvent"/> is converted to <see cref="OpenHabItemCommandReceived"/>.
/// State events extend <see cref="ValueUpdateReceived"/> while command events extend <see cref="ValueWriteReceived"/>.
/// </para>
/// <para>
/// <b>Outbound</b> (EventBus → OpenHab): Subscribes to <see cref="ValueWriteRequest"/> on the HC event bus.
/// When the source value has an <see cref="OpenHabBusEndpointMapping"/>, the value is sent to OpenHab
/// via the REST API using <see cref="IRestApiClient.SetItemStateAsync"/>.
/// </para>
/// <para>
/// <b>Value discovery</b>: At startup, all registered <see cref="IValue"/> instances from
/// <see cref="IValuesContainer"/> that carry an <see cref="OpenHabBusEndpointMapping"/> are
/// indexed by item name.
/// </para>
/// </remarks>
[ManualConnectivityProviderRegistration]
public sealed class OpenHabConnectivityProvider : ConnectivityProviderBase<string, OpenHabBusEndpointMapping>
{
    private const string ProviderName = nameof(OpenHabConnectivityProvider);
    private static readonly TimeSpan ValuesReadyWaitTimeout = TimeSpan.FromSeconds(30);
    private readonly IOptions<OpenHabIntegrationOptions> options;
    private readonly IEventPublisher _publisher;
    private readonly IEventSubscriber _subscriber;
    private readonly IEventBusClient _eventBusClient;
    private readonly IRestApiClient _restApiClient;
    private readonly IReadOnlyList<IValuesContainer> _containers;
    private readonly IHomeCompanionLifeCycleSynchronization _lifeCycleSynchronization;
    private readonly OpenHabItemMetadataCache _itemMetadataCache;
    private readonly OpenHabTypeConversionRegistry _typeConversionRegistry;
    private readonly ILogger<OpenHabConnectivityProvider> _logger;

    private volatile bool _isInitializationFinished;
    private int _firstInboundRawEventLogged;

    /// <inheritdoc/>
    public override bool IsEnabled => options.Value.Enable; // OpenHab is enabled based on the configuration

    /// <inheritdoc/>
    public override bool IsConnected => _eventBusClient.IsActive;

    /// <inheritdoc/>
    public override bool IsInitializationFinished => _isInitializationFinished;

    /// <summary>
    /// Initializes a new <see cref="OpenHabConnectivityProvider"/>.
    /// </summary>
    public OpenHabConnectivityProvider(
        IOptions<OpenHabIntegrationOptions> options,
        IEventPublisher publisher,
        IEventSubscriber subscriber,
        IEventBusClient eventBusClient,
        IRestApiClient restApiClient,
        IEnumerable<IValuesContainer> containers,
        IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization,
        OpenHabItemMetadataCache itemMetadataCache,
        OpenHabTypeConversionRegistry typeConversionRegistry,
        ILogger<OpenHabConnectivityProvider> logger)
    {
        this.options = options;
        _publisher = publisher;
        _subscriber = subscriber;
        _eventBusClient = eventBusClient;
        _restApiClient = restApiClient;
        _containers = [.. containers];
        _lifeCycleSynchronization = lifeCycleSynchronization;
        _itemMetadataCache = itemMetadataCache;
        _typeConversionRegistry = typeConversionRegistry;
        _logger = logger;
    }

    /// <inheritdoc/>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if ( !IsEnabled )
        {
            _logger.LogWarning("OpenHabConnectivityProvider is disabled via configuration. Skipping startup.");
            return;
        }

        await WaitForStartupGateAsync(
            _lifeCycleSynchronization,
            _logger,
            ProviderName,
            ValuesReadyWaitTimeout,
            cancellationToken);

        // Subscribe to outbound write requests from the event bus
        SubscribeValueWriteRequests(_subscriber, HandleValueWriteRequestAsync);

        // Discover all IValue properties with an OpenHab bus mapping
        _valueMap = DiscoverOpenHabValues();
        _logger.LogInformation("OpenHabConnectivityProvider: discovered {Count} OpenHab values.", _valueMap.Count);

        // Subscribe to incoming events from OpenHab
        _eventBusClient.EventReceived += OnEventBusClientEventReceived;

        // Explicitly request the item event types this provider consumes.
        // TODO: only enque the filter should be sufficient. Simplify.
        // Some OpenHAB websocket setups only deliver control/websocket events until a type filter is set.
        try
        {
            await _eventBusClient.SendAsync(_eventBusClient.EventFactory.CreateFilterType([
                SRF.Network.OpenHab.EventBus.EventType.ItemStateEvent,
                SRF.Network.OpenHab.EventBus.EventType.ItemStateChangedEvent,
                SRF.Network.OpenHab.EventBus.EventType.ItemCommandEvent,
            ]), cancellationToken);

            _logger.LogInformation(
                "Applied OpenHAB websocket type filter for item events via direct send: {EventTypes}.",
                "ItemStateEvent, ItemStateChangedEvent, ItemCommandEvent");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _eventBusClient.EnqueueTransmit(_eventBusClient.EventFactory.CreateFilterType([
                SRF.Network.OpenHab.EventBus.EventType.ItemStateEvent,
                SRF.Network.OpenHab.EventBus.EventType.ItemStateChangedEvent,
                SRF.Network.OpenHab.EventBus.EventType.ItemCommandEvent,
            ]));

            _logger.LogInformation(
                "Queued OpenHAB websocket type filter for item events: {EventTypes}. It will be applied once the websocket transmit loop is ready.",
                "ItemStateEvent, ItemStateChangedEvent, ItemCommandEvent");
            _logger.LogDebug(ex, "Direct type-filter send failed; queued type-filter instead.");
        }

        // Connection lifecycle is owned by OpenHabConnector (IHostedService) registered via AddOpenHabConnector().
        // Do not await ConnectAsync() here: that call is session-long and would block host startup.
        if (_eventBusClient.IsActive)
        {
            _logger.LogInformation(
                "OpenHab event bus client is already active. Inbound events should start flowing immediately.");
        }
        else
        {
            _logger.LogWarning(
                "OpenHab event bus client is not active yet. This is expected during startup: OpenHabConnector manages background (re)connect and events will flow once connected.");
        }

        _logger.LogInformation("OpenHabConnectivityProvider started and listening to event bus.");

        // Mark initialization as finished (no initial reads needed; values will be populated by incoming events)
        _isInitializationFinished = true;

        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _eventBusClient.EventReceived -= OnEventBusClientEventReceived;
        _logger.LogInformation("OpenHabConnectivityProvider stopped.");
        await Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Value discovery
    // -------------------------------------------------------------------------

    private Dictionary<string, ValueMapping<OpenHabBusEndpointMapping>> DiscoverOpenHabValues()
    {
        var map = BuildValueMap(
            _containers,
            OpenHabBusEndpointMapping.BusId,
            mapping => mapping.ItemName,
            StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation("Discovered {Count} OpenHab values.", map.Count);
        return map;
    }

    // -------------------------------------------------------------------------
    // Inbound: OpenHab → EventBus
    // -------------------------------------------------------------------------

    private void OnEventBusClientEventReceived(object? sender, EventReceivedEventArgs e)
    {
        LogFirstInboundAfterStartupGate(_logger, ProviderName, "event");

        if (Interlocked.CompareExchange(ref _firstInboundRawEventLogged, 1, 0) == 0)
        {
            _logger.LogInformation(
                "First inbound OpenHAB websocket event received ({EventType}).",
                e.Received.GetType().Name);
        }

        switch (e.Received)
        {
            case ItemStateChangedEvent stateChanged:
                PublishItemStateChanged(stateChanged);
                break;

            case ItemEventTypeValue valueEvent when valueEvent.Type == SRF.Network.OpenHab.EventBus.EventType.ItemStateEvent:
                PublishItemState(valueEvent);
                break;

            case ItemEventTypeValue valueEvent when valueEvent.Type == SRF.Network.OpenHab.EventBus.EventType.ItemCommandEvent:
                PublishItemCommand(valueEvent);
                break;
        }
    }

    private void PublishItemStateChanged(ItemStateChangedEvent stateChanged)
    {
        var itemName = stateChanged.ItemName;
        var stateChange = stateChanged.StateChange;
        var target = ResolveTarget(itemName);

        if (target is null)
        {
            _logger.LogTrace("Received ItemStateChangedEvent for item '{ItemName}' with new state '{NewState}', but no target value found. Skipping.", itemName, stateChange.Value);
            return;
        }
        if (!target.Mapping.Communication.HasFlag(BusCommunication.Receive))
        {
            //_logger.LogTrace("Received ItemStateChangedEvent for item '{ItemName}' with new state '{NewState}', but target value does not allow read communication. Skipping.", itemName, stateChange.Value);
            return;
        }

        var decodedValue = ConvertStateValue(itemName, stateChange.Value, stateChange.Type, target?.Value);

        _ = _publisher.PublishAsync(new OpenHabItemStateChanged
        {
            ItemName = itemName,
            RawState = stateChange.Value,
            OldRawState = stateChange.OldValue,
            Value = decodedValue,
            Target = target?.Value,
            Timestamp = DateTimeOffset.UtcNow,
        });
    }

    private void PublishItemState(ItemEventTypeValue stateEvent)
    {
        var itemName = stateEvent.ItemName;
        var rawState = stateEvent.State.Value;
        var target = ResolveTarget(itemName);

        if (target is null)
        {
            _logger.LogTrace("Received ItemStateEvent for item '{ItemName}' with state '{State}', but no target value found. Skipping.", itemName, rawState);
            return;
        }
        if (!target.Mapping.Communication.HasFlag(BusCommunication.Receive))
        {
            //_logger.LogTrace("Received ItemStateEvent for item '{ItemName}' with state '{State}', but target value does not allow read communication. Skipping.", itemName, rawState);
            return;
        }

        var decodedValue = ConvertStateValue(itemName, rawState, stateEvent.State.Type, target?.Value);

        _ = _publisher.PublishAsync(new OpenHabItemState
        {
            ItemName = itemName,
            RawState = rawState,
            Value = decodedValue,
            Target = target?.Value,
            Timestamp = DateTimeOffset.UtcNow,
        });
    }

    private void PublishItemCommand(ItemEventTypeValue commandEvent)
    {
        var itemName = commandEvent.ItemName;
        var rawCommand = commandEvent.State.Value;
        var target = ResolveTarget(itemName);

        if (target is null)
        {
            _logger.LogTrace("Received ItemCommandEvent for item '{ItemName}' with command '{Command}', but no target value found. Skipping.", itemName, rawCommand);
            return;
        }
        if (!target.Mapping.Communication.HasFlag(BusCommunication.Receive))
        {
            _logger.LogTrace("Received ItemCommandEvent for item '{ItemName}' with command '{Command}', but target value does not allow read communication. Skipping.", itemName, rawCommand);
            return;
        }

        var decodedValue = ConvertStateValue(itemName, rawCommand, commandEvent.State.Type, target?.Value);

        _ = _publisher.PublishAsync(new OpenHabItemCommandReceived
        {
            ItemName = itemName,
            RawCommand = rawCommand,
            Value = decodedValue,
            Target = target?.Value,
            Timestamp = DateTimeOffset.UtcNow,
        });
    }

    private object? ConvertStateValue(string itemName, string rawState, string? stateType, IValue? target)
    {
        if (target is null)
        {
            _logger.LogTrace("No target value found for OpenHab item '{ItemName}'. Cannot convert state '{State}'.", itemName, rawState);
            return null;
        }

        _itemMetadataCache.TryGetItem(itemName, out var itemMetadata);
        var localConfig = target.TryGetBusEndpoint<OpenHabBusEndpointMapping>(OpenHabBusEndpointMapping.BusId, out var mapping)
            ? mapping?.Config
            : null;

        if (_typeConversionRegistry.TryConvertValue(rawState, target, stateType, itemMetadata, localConfig, out var decodedValue, out var errorMessage))
            return decodedValue;

        AddConversionFailure(itemName, rawState, stateType, target, errorMessage);
        _logger.LogDebug("State conversion failed for OpenHab item '{ItemName}' with value '{State}'.", itemName, rawState);
        return null;
    }

    private static void AddConversionFailure(string itemName, string rawState, string? stateType, IValue target, string? errorMessage)
    {
        if (target is not ValueBase valueBase)
            return;

        var message = string.IsNullOrWhiteSpace(errorMessage)
            ? $"OpenHAB state conversion failed for item '{itemName}' with state '{rawState}'."
            : $"OpenHAB state conversion failed for item '{itemName}' with state '{rawState}': {errorMessage}";

        if (target.TryGetBusEndpoint<OpenHabBusEndpointMapping>(OpenHabBusEndpointMapping.BusId, out var mapping) && mapping is not null)
        {
            valueBase.AddException(new ValueReceptionException(itemName, mapping, message, new FormatException(message)));
            return;
        }

        valueBase.AddException(new ValueException(message, new FormatException(message)));
    }

    // -------------------------------------------------------------------------
    // Outbound: EventBus → OpenHab
    // -------------------------------------------------------------------------

    private async Task HandleValueWriteRequestAsync(ValueWriteRequest request, CancellationToken cancellationToken)
    {
        if (!request.Source.TryGetBusEndpoint<OpenHabBusEndpointMapping>(OpenHabBusEndpointMapping.BusId, out var mapping))
            return; // not an OpenHab-backed value

        if ( !(mapping?.Communication.HasFlag(BusCommunication.Transmit) ?? false) )
        {
            //_logger.LogTrace("Received ValueWriteRequest for '{ValueName}', but its OpenHab mapping does not allow send communication. Skipping.", request.Source.Name);
            return;
        }

        var itemName = mapping!.ItemName;

        if (request.NewValue is null)
        {
            _logger.LogWarning("ValueWriteRequest for '{ItemName}': value is null, skipping send.", itemName);
            return;
        }

        try
        {
            var stateString = FormatStateForOutboundWrite(request.Source, request.NewValue);
            await _restApiClient.SetItemStateAsync(itemName, stateString, cancellationToken);
            _logger.LogDebug("Sent OpenHab write request for '{ItemName}' with value '{State}'.", itemName, stateString);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send OpenHab write request for '{ItemName}' with value '{Value}'.", itemName, request.NewValue);
        }
    }

    private string FormatStateForOutboundWrite(IValue source, object value)
    {
        if (value is null)
            return string.Empty;

        Item? itemMetadata = null;
        OpenHabBusMappingConfiguration? config = null;

        if (source.TryGetBusEndpoint<OpenHabBusEndpointMapping>(OpenHabBusEndpointMapping.BusId, out var mapping))
        {
            config = mapping?.Config;
            if (mapping is not null)
                _itemMetadataCache.TryGetItem(mapping.ItemName, out itemMetadata);
        }

        return _typeConversionRegistry.FormatOutboundValue(source, value, itemMetadata, config);
    }
}
