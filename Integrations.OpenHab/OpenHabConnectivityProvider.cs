using HomeCompanion.Events;
using HomeCompanion.Integrations.OpenHab.Events;
using HomeCompanion.Abstractions;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using SRF.Network.OpenHab;
using SRF.Network.OpenHab.Client;
using SRF.Network.OpenHab.EventBus.Events;
using System.Globalization;
using System.Reflection;

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
/// <b>Value discovery</b>: At startup, all <see cref="IValue"/> properties (any visibility, any depth) on
/// registered <see cref="IValuesContainer"/> instances that carry an <see cref="OpenHabBusEndpointMapping"/> are
/// discovered via reflection and indexed by item name.
/// </para>
/// </remarks>
public sealed class OpenHabConnectivityProvider : IConnectivityProvider
{
    private static readonly TimeSpan ValuesReadyWaitTimeout = TimeSpan.FromSeconds(30);

    private readonly IEventPublisher _publisher;
    private readonly IEventSubscriber _subscriber;
    private readonly IEventBusClient _eventBusClient;
    private readonly IRestApiClient _restApiClient;
    private readonly IEnumerable<IValuesContainer> _containers;
    private readonly IHomeCompanionLifeCycleSynchronization _lifeCycleSynchronization;
    private readonly OpenHabStateConverter _stateConverter;
    private readonly ILogger<OpenHabConnectivityProvider> _logger;

    private record OpenHabItemValueMapping(string ItemName, IValue Value, OpenHabBusEndpointMapping Mapping);

    /// <summary>Item name → registered value map, built at startup.</summary>
    private Dictionary<string, OpenHabItemValueMapping> _valueMap = [];

    private volatile bool _isInitializationFinished;
    private volatile bool _isConnected;
    private DateTimeOffset? _inboundGateReleasedAt;
    private long _inboundEventsSeen;

    /// <inheritdoc/>
    public bool IsEnabled => true; // OpenHab is always enabled if the provider is instantiated

    /// <inheritdoc/>
    public bool IsConnected => _isConnected;

    /// <inheritdoc/>
    public bool IsInitializationFinished => _isInitializationFinished;

    /// <summary>
    /// Initializes a new <see cref="OpenHabConnectivityProvider"/>.
    /// </summary>
    public OpenHabConnectivityProvider(
        IEventPublisher publisher,
        IEventSubscriber subscriber,
        IEventBusClient eventBusClient,
        IRestApiClient restApiClient,
        IEnumerable<IValuesContainer> containers,
        IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization,
        OpenHabStateConverter stateConverter,
        ILogger<OpenHabConnectivityProvider> logger)
    {
        _publisher = publisher;
        _subscriber = subscriber;
        _eventBusClient = eventBusClient;
        _restApiClient = restApiClient;
        _containers = containers;
        _lifeCycleSynchronization = lifeCycleSynchronization;
        _stateConverter = stateConverter;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var waitStartedAt = DateTimeOffset.UtcNow;
        _logger.LogDebug("OpenHabConnectivityProvider waiting for stage {Stage} before enabling inbound event processing. Timeout={Timeout}.", AppInitializationStage.InitValuesRegistered, ValuesReadyWaitTimeout);
        await _lifeCycleSynchronization.WaitForInitializationStageCompletedAsync(AppInitializationStage.InitValuesRegistered, ValuesReadyWaitTimeout, cancellationToken);
        _inboundGateReleasedAt = DateTimeOffset.UtcNow;
        _logger.LogInformation(
            "OpenHabConnectivityProvider startup gate released after {ElapsedMs} ms (stage {Stage}).",
            (_inboundGateReleasedAt.Value - waitStartedAt).TotalMilliseconds,
            AppInitializationStage.InitValuesRegistered);

        // Subscribe to outbound write requests from the event bus
        _subscriber.Subscribe(new ValueWriteRequestHandler(this));

        // Discover all IValue properties with an OpenHab bus mapping
        _valueMap = DiscoverOpenHabValues();
        _logger.LogInformation("OpenHabConnectivityProvider: discovered {Count} OpenHab values.", _valueMap.Count);

        // Subscribe to incoming events from OpenHab
        _eventBusClient.EventReceived += OnEventBusClientEventReceived;

        // Mark as connected (OpenHab connectivity is managed by OpenHabConnector)
        _isConnected = true;
        _logger.LogInformation("OpenHabConnectivityProvider started and listening to event bus.");

        // Mark initialization as finished (no initial reads needed; values will be populated by incoming events)
        _isInitializationFinished = true;

        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _eventBusClient.EventReceived -= OnEventBusClientEventReceived;
        _isConnected = false;
        _logger.LogInformation("OpenHabConnectivityProvider stopped.");
        await Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Value discovery
    // -------------------------------------------------------------------------

    private Dictionary<string, OpenHabItemValueMapping> DiscoverOpenHabValues()
    {
        var map = new Dictionary<string, OpenHabItemValueMapping>();
        foreach (var container in _containers)
            DiscoverOpenHabValuesIn(container, map);
        _logger.LogInformation("Discovered {Count} OpenHab values.", map.Count);
        return map;
    }

    private void DiscoverOpenHabValuesIn(object instance, Dictionary<string, OpenHabItemValueMapping> map)
    {
        var type = instance.GetType();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => typeof(IValue).IsAssignableFrom(p.PropertyType));
        foreach (var prop in properties)
        {
            if (!prop.CanRead) continue;

            if (prop.GetValue(instance) is not IValue value) continue;
            if (!value.TryGetBusEndpoint<OpenHabBusEndpointMapping>(OpenHabBusEndpointMapping.BusId, out var mapping)) continue;

            var itemName = mapping!.ItemName;
            if (map.ContainsKey(itemName))
                _logger.LogWarning(
                    "Duplicate OpenHab item name '{ItemName}' found on property '{Prop}' of {Type}; already registered by another value. Skipping.",
                    itemName, prop.Name, type.FullName);
            else
                map[itemName] = new OpenHabItemValueMapping(itemName, value, mapping);
        }
    }

    // -------------------------------------------------------------------------
    // Inbound: OpenHab → EventBus
    // -------------------------------------------------------------------------

    private OpenHabItemValueMapping? ResolveTarget(string itemName)
    {
        if (_valueMap.TryGetValue(itemName, out var target))
            return target;
        return null;
    }

    private void OnEventBusClientEventReceived(object? sender, EventReceivedEventArgs e)
    {
        if (Interlocked.Increment(ref _inboundEventsSeen) == 1 && _inboundGateReleasedAt is { } gateReleasedAt)
        {
            _logger.LogDebug(
                "OpenHabConnectivityProvider received first inbound event {ElapsedMs} ms after startup gate release.",
                (DateTimeOffset.UtcNow - gateReleasedAt).TotalMilliseconds);
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

        var decodedValue = ConvertStateValue(itemName, stateChange.Value, target?.Value);

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
            _logger.LogTrace("Received ItemStateEvent for item '{ItemName}' with state '{State}', but target value does not allow read communication. Skipping.", itemName, rawState);
            return;
        }

        var decodedValue = ConvertStateValue(itemName, rawState, target?.Value);

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

        var decodedValue = ConvertStateValue(itemName, rawCommand, target?.Value);

        _ = _publisher.PublishAsync(new OpenHabItemCommandReceived
        {
            ItemName = itemName,
            RawCommand = rawCommand,
            NewValue = decodedValue,
            Target = target?.Value,
            Timestamp = DateTimeOffset.UtcNow,
        });
    }

    private object? ConvertStateValue(string itemName, string rawState, IValue? target)
    {
        if (target is null)
        {
            _logger.LogTrace("No target value found for OpenHab item '{ItemName}'. Cannot convert state '{State}'.", itemName, rawState);
            return null;
        }

        if (_stateConverter.TryConvertValue(rawState, target, out var decodedValue))
            return decodedValue;

        if (TryConvertByTargetType(rawState, target.ValueType, out decodedValue))
            return decodedValue;

        _logger.LogDebug("State conversion failed for OpenHab item '{ItemName}' with value '{State}'.", itemName, rawState);
        return null;
    }

    private static bool TryConvertByTargetType(string rawState, Type targetType, out object? converted)
    {
        converted = null;

        var nonNullable = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (nonNullable == typeof(string))
        {
            converted = rawState;
            return true;
        }

        if (nonNullable == typeof(bool))
        {
            if (rawState.Equals("ON", StringComparison.OrdinalIgnoreCase) || rawState.Equals("OPEN", StringComparison.OrdinalIgnoreCase) || rawState.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            {
                converted = true;
                return true;
            }

            if (rawState.Equals("OFF", StringComparison.OrdinalIgnoreCase) || rawState.Equals("CLOSED", StringComparison.OrdinalIgnoreCase) || rawState.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            {
                converted = false;
                return true;
            }

            return false;
        }

        if (nonNullable.IsEnum)
        {
            if (Enum.TryParse(nonNullable, rawState, ignoreCase: true, out var enumValue))
            {
                converted = enumValue;
                return true;
            }

            return false;
        }

        try
        {
            converted = Convert.ChangeType(rawState, nonNullable, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
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
            _logger.LogTrace("Received ValueWriteRequest for '{ValueName}', but its OpenHab mapping does not allow send communication. Skipping.", request.Source.Name);
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
            var stateString = request.NewValue.ToString() ?? "";
            await _restApiClient.SetItemStateAsync(itemName, stateString, cancellationToken);
            _logger.LogDebug("Sent OpenHab write request for '{ItemName}' with value '{State}'.", itemName, stateString);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OpenHab write request for '{ItemName}' with value '{Value}'.", itemName, request.NewValue);
        }
    }

    // -------------------------------------------------------------------------
    // Inner event handler
    // -------------------------------------------------------------------------

    private sealed class ValueWriteRequestHandler : IEventHandler<ValueWriteRequest>
    {
        private readonly OpenHabConnectivityProvider _provider;
        public ValueWriteRequestHandler(OpenHabConnectivityProvider provider) => _provider = provider;
        public ValueTask HandleAsync(ValueWriteRequest @event, CancellationToken cancellationToken = default)
            => new(_provider.HandleValueWriteRequestAsync(@event, cancellationToken));
    }
}
