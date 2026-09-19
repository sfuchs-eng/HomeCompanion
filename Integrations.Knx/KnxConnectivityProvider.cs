using HomeCompanion.Events;
using HomeCompanion.Values;
using HomeCompanion.Integrations.Knx.Events;
using Microsoft.Extensions.Logging;
using SRF.Knx.Core;
using SRF.Network.Knx;
using SRF.Network.Knx.Messages;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SRF.Knx.Config;
using SRF.Knx.Core.DPT;
using HomeCompanion.Abstractions;
using HomeCompanion.Persistence;
using System.Globalization;

namespace HomeCompanion.Integrations.Knx;

/// <summary>
/// KNX connectivity provider. Bridges one or more <see cref="IKnxConnection"/> instances to the
/// HomeCompanion event bus.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inbound</b> (KNX → EventBus): One event is published per received telegram:
/// <list type="bullet">
///   <item><c>GroupValueWrite</c> → <see cref="KnxGroupWriteReceived"/> (extends <see cref="ValueUpdateReceived"/>)</item>
///   <item><c>GroupValueRead</c> → <see cref="KnxGroupReadReceived"/> (extends <see cref="ValueReadReceived"/>)</item>
///   <item><c>GroupValueResponse</c> → <see cref="KnxGroupResponseReceived"/> (extends <see cref="ValueReadAnswerReceived"/>)</item>
/// </list>
/// Events are published for every received telegram. <see cref="ValueUpdateReceived.Target"/> (and equivalents) is the
/// registered <see cref="IValue"/> for the group address, or <see langword="null"/> if none is mapped.
/// Subscribers to a base type (e.g. <see cref="ValueUpdateReceived"/>) receive derived events via the type-hierarchy
/// dispatch of the event bus.
/// </para>
/// <para>
/// <b>Outbound</b> (EventBus → KNX): Subscribes to <see cref="ValueWriteRequest"/> on the HC event bus.
/// When the source value has a <see cref="KnxBusEndpointMapping"/>, the value is encoded and broadcast
/// as a <c>GroupValueWrite</c> telegram to all registered connections.
/// </para>
/// <para>
/// <b>Value discovery</b>: At startup, all <see cref="IValue"/> properties (any visibility, any depth) on
/// registered <see cref="IValuesContainer"/> instances that carry a <see cref="KnxBusEndpointMapping"/> are
/// discovered via reflection and indexed by its
/// <see cref="KnxBusEndpointMapping.GroupAddress"/>. A <c>GroupValueRead</c> is sent for each registered
/// group address so that the bus can respond with the current value, completing initial value population.
/// </para>
/// </remarks>
public sealed class KnxConnectivityProvider : ConnectivityProviderBase<GroupAddress, KnxBusEndpointMapping>
{
    private const string ProviderName = nameof(KnxConnectivityProvider);
    private static readonly TimeSpan InitializationReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ValuesReadyWaitTimeout = TimeSpan.FromSeconds(30);

    private readonly IReadOnlyList<IKnxConnection> _connections;
    private readonly KnxIntegrationOptions _integrationOptions;
    private readonly IKnxSystemConfiguration knxSystemConfiguration;
    private readonly IEnumerable<IKnxConnection> connections;
    private readonly IEventPublisher _publisher;
    private readonly IEventSubscriber _subscriber;
    private readonly IEnumerable<IValuesContainer> containers;
    private readonly IHomeCompanionLifeCycleSynchronization lifeCycleSync;
    private readonly IStateInitializationRegistrar stateInitializationManager;
    private readonly IReadOnlyList<IValuesContainer> _containers;
    private readonly IDptResolver _dptResolver;
    private readonly ILogger<KnxConnectivityProvider> _logger;

    /// <summary>Tracks which group addresses still need an initial read response.</summary>
    private readonly ConcurrentDictionary<GroupAddress, bool> _pendingInitialReads = [];

    private volatile bool _isInitializationFinished;

    /// <inheritdoc/>
    public override bool IsEnabled => _integrationOptions.Enable && _connections.Count > 0;

    /// <inheritdoc/>
    public override bool IsConnected => _connections.Any(c => c.IsConnected);

    private Task ValueInitializationTask = Task.CompletedTask; // placeholder task to track when initial value population is finished, so that ValueReadReceived events can await it to ensure values are populated before responding to read requests
    private CancellationTokenSource? ValueInitializationCts; // separate CTS to allow canceling the initialization wait if needed, e.g. on shutdown

    /// <inheritdoc/>
    public override bool IsInitializationFinished => _isInitializationFinished;

    /// <summary>
    /// Initializes a new <see cref="KnxConnectivityProvider"/>.
    /// </summary>
    public KnxConnectivityProvider(
        IOptions<KnxIntegrationOptions> integrationOptions,
        IKnxSystemConfiguration knxSystemConfiguration,
        IEnumerable<IKnxConnection> connections,
        IEventPublisher publisher,
        IEventSubscriber subscriber,
        IEnumerable<IValuesContainer> containers,
        IHomeCompanionLifeCycleSynchronization lifeCycleSync,
        IStateInitializationRegistrar stateInitializationManager,
        IDptResolver dptResolver,
        ILogger<KnxConnectivityProvider> logger)
    {
        _connections = [.. connections];
        _integrationOptions = integrationOptions.Value;
        this.knxSystemConfiguration = knxSystemConfiguration;
        this.connections = connections;
        _publisher = publisher;
        _subscriber = subscriber;
        this.containers = containers;
        this.lifeCycleSync = lifeCycleSync;
        this.stateInitializationManager = stateInitializationManager;
        _containers = [.. containers];
        _dptResolver = dptResolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await WaitForStartupGateAsync(
            lifeCycleSync,
            _logger,
            ProviderName,
            ValuesReadyWaitTimeout,
            cancellationToken);

        // Subscribe to outbound write requests from the event bus
        SubscribeValueWriteRequests(_subscriber, HandleValueWriteRequestAsync);

        // Discover all IValue properties with a KNX bus mapping
        _valueMap = DiscoverKnxValues();
        _logger.LogInformation("KnxConnectivityProvider: discovered {Count} KNX values across {ContainerCount} containers.",
            _valueMap.Count, _containers.Count);

        // Connect all buses
        foreach (var connection in _connections)
        {
            connection.MessageReceived += OnMessageReceived;
            connection.ConnectionStatusChanged += OnConnectionStatusChanged;
            await connection.ConnectAsync(cancellationToken);
        }

        // spawn a background task to initialize all values by sending GroupValueRead for each registered group address and waiting for responses, while allowing the provider to be marked as "initialization finished" after a timeout even if some responses are missing
        ValueInitializationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ValueInitializationTask = Task.Run(() => SendInitialReadRequestsAndMonitorAsync(ValueInitializationCts.Token), cancellationToken);
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // in case the initialization is still running, cancel it to avoid waiting for missing read responses during shutdown
        ValueInitializationCts?.Cancel();
        try { await ValueInitializationTask; }
        catch (OperationCanceledException) { /* expected on cancellation, ignore */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KNX value initialization task failed during shutdown.");
        }

        foreach (var connection in _connections)
        {
            connection.MessageReceived -= OnMessageReceived;
            connection.ConnectionStatusChanged -= OnConnectionStatusChanged;

            try
            {
                await connection.DisconnectAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Disconnecting KNX connection was canceled by host shutdown token.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Disconnecting KNX connection canceled during shutdown.");
            }
            catch (ObjectDisposedException)
            {
                _logger.LogDebug("KNX connection already disposed during shutdown.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to disconnect KNX connection during shutdown.");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Value discovery
    // -------------------------------------------------------------------------

    private Dictionary<GroupAddress, ValueMapping<KnxBusEndpointMapping>> DiscoverKnxValues()
    {
        return BuildValueMap(
            _containers,
            KnxBusEndpointMapping.BusId,
            mapping => mapping.GroupAddress);
    }

    // -------------------------------------------------------------------------
    // Initial read requests, typically a background task at startup to populate values with current bus state
    // -------------------------------------------------------------------------

    private async Task SendInitialReadRequestsAndMonitorAsync(CancellationToken cancellationToken)
    {
        if ( !_integrationOptions.CommunicationPermissions.HasFlag(CommunicationPermissions.RxGroupAddressReadAnswers | CommunicationPermissions.TxGroupAddressReads) )
        {
            _logger.LogInformation("Skipping initial read requests for KNX values because communication permissions do not allow receiving read answers or transmitting read requests.");
            _isInitializationFinished = true;
            return;
        }
        if ( !_integrationOptions.ReadGroupAddressesOnStartup || _valueMap.Count == 0)
        {
            _logger.LogInformation("Skipping initial read requests for KNX values because ReadGroupAddressesOnStartup is disabled or no values were discovered.");
            _isInitializationFinished = true;
            return;
        }

        // wait until the right initialization level is reached.
        await lifeCycleSync.WaitForInitializationStageCompletedAsync(AppLifeCycleStage.InitLoadFromStore, TimeSpan.FromSeconds(10), cancellationToken);

        // wait until all KNX connections are established before sending initial read requests
        while (!_connections.All(c => c.IsConnected))
            await Task.Delay(200, cancellationToken);

        var dptToSkipReading = new[] {
            "DPST-1-15", // Reset
            "DPST-1-17", // Trigger
            "DPT-3",      // shutter moves
            "DPST-5-10", // 8bit trigger counter pulses
            "DPST-1-16", // Acknowledge
            "DPST-17-1", // Scene control
            "DPST-18-1", // Scene control
        }.Select(s => knxSystemConfiguration.GetDptFromId(s))
        .ToArray(); // reading these doesn't make much sense, as they typically represent momentary events (e.g. button press) where the current value is not relevant and might not even be updated on the bus

        foreach (var ga in _valueMap.Keys)
        {
            // no Initialize communication for this GA according to its bus mapping configuration?
            if (!_valueMap[ga].Mapping.Communication.HasFlag(BusCommunication.Initialize))
            {
                _logger.LogTrace("Skipping initial read request for {GA} because its KnxBusEndpointMapping does not allow initialize communication.", ga);
                continue;
            }

            // DPT to be skipped generally?
            try
            {
                var dpt = knxSystemConfiguration.GetDpt(ga);
                if (dptToSkipReading.Any(d => d.GetType() == dpt.GetType()))
                {
                    _logger.LogTrace("Skipping initial read request for {GA} with DPT {DPT} as it's configured to be skipped.", ga, dpt.GetType().Name);
                    _pendingInitialReads.TryRemove(ga, out _);
                    continue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get DPT for {GA} during initial read request. Sending read request anyway.", ga);
            }

            _pendingInitialReads.TryAdd(ga, true);
        }

        // register answer handlers before sending read requests
        foreach (var connection in _connections)
            connection.MessageReceived += ProcessInitialReadResponse;

        // send read requests with some delay in between to avoid overwhelming the bus at startup, especially if there are many group addresses to read
        foreach (var ga in _pendingInitialReads.Keys.ToArray()) // ToArray to avoid collection modified issues as we remove from the dictionary when responses come in
        {
            // does the bus mapping allow for communication?
            var mapping = _valueMap[ga].Mapping;

            // send read request
            var readRequest = new GroupMessageRequest(ga, new SRF.Knx.Core.GroupValue(), GroupEventType.ValueRead);
            foreach (var connection in _connections)
            {
                try { await connection.SendMessageAsync(readRequest, cancellationToken); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to send initial read request for {GA}.", ga); }
            }
            await Task.Delay(250, cancellationToken);
        }

        // Wait up to InitializationReadTimeout for all GAs to respond
        using var timeoutCts = new CancellationTokenSource(InitializationReadTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            while (!_pendingInitialReads.IsEmpty)
                await Task.Delay(200, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "KNX initialization read timeout reached. {Count} group address(es) did not respond: {GAs}",
                _pendingInitialReads.Count,
                string.Join(", ", _pendingInitialReads.Keys));
        }

        // unregister answer handlers
        foreach (var connection in _connections)
            connection.MessageReceived -= ProcessInitialReadResponse;

        // _pendinginitialReads should now be empty, but just in case, clear it to free memory and avoid potential future confusion
        _pendingInitialReads.Clear();

        _isInitializationFinished = true;
    }

    private void ProcessInitialReadResponse(object? sender, KnxMessageReceivedEventArgs e)
    {
        if (!_integrationOptions.CommunicationPermissions.HasFlag(CommunicationPermissions.RxGroupAddressReadAnswers))
            return; // if the communication permissions don't allow receiving read answers, ignore all responses
            
        // Whatever sends backa value suitable for initializing we consider accordingly. Means Write and ReadAnswer telegrams, but not Read requests.
        switch (e.KnxMessageContext.GroupEventArgs?.EventType)
        {
            case GroupEventType.ValueWrite:
            case GroupEventType.ValueResponse:
                if (e.KnxMessageContext.GroupEventArgs.DestinationAddress is { } ga)
                    _pendingInitialReads.TryRemove(ga, out _);
                if (!_valueMap.TryGetValue(e.KnxMessageContext.GroupEventArgs.DestinationAddress, out var mapping))
                    break; // no registered value for this GA, ignore
                if ((mapping.Mapping.Communication & (BusCommunication.Initialize | BusCommunication.Receive)) > 0 && e.KnxMessageContext.DecodedValue is not null)
                {
                    try
                    {
                        var dpt = _dptResolver.GetDpt(e.KnxMessageContext.GroupEventArgs.DestinationAddress);
                        var normalizedValue = NormalizeInboundValue(mapping.Value, e.KnxMessageContext.DecodedValue, dpt);
                        mapping.Value.InitializeValue(normalizedValue ?? e.KnxMessageContext.DecodedValue, AppLifeCycleStage.InitBusValueReceived);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to initialize value for {GA}.", e.KnxMessageContext.GroupEventArgs.DestinationAddress);
                    }
                }
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Inbound: KNX → EventBus
    // -------------------------------------------------------------------------

    private void OnMessageReceived(object? sender, KnxMessageReceivedEventArgs e)
    {
        LogFirstInboundAfterStartupGate(_logger, ProviderName, "telegram");

        var ctx = e.KnxMessageContext;
        if (ctx.GroupEventArgs is not { } args) return;

        // Lookup registered IValue for this GA — null when no mapping exists.
        // Events are always published; Target being null allows bus-aware listeners to observe
        // telegrams for group addresses that have no corresponding IValue registered.
        var target = ResolveTarget(args.DestinationAddress);
        var dpt = args.DestinationAddress is not null ? _dptResolver.GetDpt(args.DestinationAddress) : null;
        var normalizedValue = NormalizeInboundValue(target?.Value, ctx.DecodedValue, dpt);

        if ( args?.DestinationAddress is null )
        {
            _logger.LogWarning("Received KNX telegram with null destination address. Ignoring.");
            return;
        }

        switch (args.EventType)
        {
            case GroupEventType.ValueWrite:
                if (!_integrationOptions.CommunicationPermissions.HasFlag(CommunicationPermissions.RxGroupAdddressWrites))
                    break;
                if (!(target?.Mapping.Communication.HasFlag(BusCommunication.Receive) ?? false))
                    break; // if the mapping doesn't allow receiving, ignore the write
                _ = _publisher.PublishAsync(new KnxGroupWriteReceived
                {
                    DestinationAddress = args.DestinationAddress,
                    SourceAddress = args.SourceAddress,
                    RawValue = args.Value,
                    DecodedValue = ctx.DecodedValue,
                    Value = normalizedValue,
                    Timestamp = ctx.ReceivedAt,
                    Target = target?.Value,
                });
                break;

            case GroupEventType.ValueRead:
                if (!_integrationOptions.CommunicationPermissions.HasFlag(CommunicationPermissions.RxGroupAddressReads))
                    break;
                if (!(target?.Mapping.Communication.HasFlag(BusCommunication.AnswerReadRequests) ?? false))
                    break; // if the mapping doesn't allow answering read requests, ignore the read
                // Send answers in a background tastk in fire-and-forget manner with a timeout to avoid blocking the KNX message processing thread in case of issues with the bus or the value initialization, as we don't want to risk missing further incoming telegrams which could also be relevant for initialization (e.g. if the initial read request triggered a response with an unexpected DPT that causes decoding to fail and thus value initialization to be skipped, but the value is still registered and can be updated by subsequent telegrams)
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                foreach (var connection in _connections)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var responseValue = target?.Value?.OValue ?? throw new Exception($"Target value for {args.DestinationAddress} is null, cannot answer read request.");
                            var dpt = _dptResolver.GetDpt(args.DestinationAddress);
                            responseValue = ConvertOutboundValue(responseValue, dpt);
                            var encodedValue = dpt.ToGroupValue(responseValue);

                            var responseMessage = new GroupMessageRequest(args.DestinationAddress, encodedValue, GroupEventType.ValueResponse);
                            await connection.SendMessageAsync(responseMessage, cts.Token);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Failed to send KNX read response for {GA} from IValue {ValueName}.", args.DestinationAddress, target?.Value?.Name ?? "<null>");
                        }
                    }, cts.Token);
                }

                // raise to event bus.
                _ = _publisher.PublishAsync(new KnxGroupReadReceived
                {
                    DestinationAddress = args.DestinationAddress,
                    SourceAddress = args.SourceAddress,
                    Timestamp = ctx.ReceivedAt,
                    Target = target?.Value,
                });
                break;

            case GroupEventType.ValueResponse:
                if (!_integrationOptions.CommunicationPermissions.HasFlag(CommunicationPermissions.RxGroupAddressReadAnswers))
                    break;
                if (!(target?.Mapping.Communication.HasFlag(BusCommunication.Receive) ?? false))
                    break; // if the mapping doesn't allow receiving, ignore the response
                _ = _publisher.PublishAsync(new KnxGroupResponseReceived
                {
                    DestinationAddress = args.DestinationAddress,
                    SourceAddress = args.SourceAddress,
                    RawValue = args.Value,
                    DecodedValue = ctx.DecodedValue,
                    Value = normalizedValue,
                    Timestamp = ctx.ReceivedAt,
                    Target = target?.Value,
                });
                //_pendingInitialReads.TryRemove(args.DestinationAddress, out _); // no need to do this here, as it's already handled in the dedicated initial read response handler that is only registered during initialization phase
                break;
        }
    }

    private void OnConnectionStatusChanged(object? sender, KnxConnectionEventArgs e)
    {
        _logger.LogInformation("KNX connection status changed. IsConnected={IsConnected}", IsConnected);
    }

    // -------------------------------------------------------------------------
    // Outbound: EventBus → KNX
    // -------------------------------------------------------------------------

    private async Task HandleValueWriteRequestAsync(ValueWriteRequest request, CancellationToken cancellationToken)
    {
        if ( !_integrationOptions.CommunicationPermissions.HasFlag(CommunicationPermissions.TxGroupAddressWrites) )
            return; // if the communication permissions don't allow writing, ignore all write requests
        if (!request.Source.TryGetBusEndpoint<KnxBusEndpointMapping>(KnxBusEndpointMapping.BusId, out var mapping))
            return; // not a KNX-backed value

        var ga = mapping!.GroupAddress;

        SRF.Knx.Core.GroupValue encoded;
        try
        {
            var dpt = _dptResolver.GetDpt(ga);
            if (request.NewValue is null)
            {
                _logger.LogWarning("ValueWriteRequest for {GA}: value is null, skipping send.", ga);
                AddValueException(request.Source, $"KNX write skipped for {ga}: value is null.");
                return;
            }

            if (!dpt.IsScaledNumeric
                && !typeof(UnitsNet.IQuantity).IsAssignableFrom(dpt.ApplicationType)
                && request.Source.ValueType != dpt.ApplicationType
                && !(typeof(UnitsNet.IQuantity).IsAssignableFrom(request.Source.ValueType) && IsNumericTargetType(dpt.ApplicationType)))
            {
                var errorMessage = $"KNX write skipped for {ga}: IValue type {request.Source.ValueType.FullName} does not match non-scaled DPT application type {dpt.ApplicationType.FullName}.";
                _logger.LogWarning(errorMessage);
                AddValueException(request.Source, errorMessage);
                return;
            }

            encoded = dpt.ToGroupValue(ConvertOutboundValue(request.NewValue, dpt));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ValueWriteRequest for {GA}: DPT encoding failed, skipping send.", ga);
            AddValueException(request.Source, $"KNX write skipped for {ga}: DPT encoding failed ({ex.Message}).", ex);
            return;
        }

        var message = new GroupMessageRequest(ga, encoded, GroupEventType.ValueWrite);
        foreach (var connection in _connections)
        {
            try { await connection.SendMessageAsync(message, cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send KNX write to {GA} on connection {Connection}.", ga, connection); }
        }
    }

    /// <summary>
    /// IValue<T> type to KNX DPT native type conversion for outbound values. For example, a UnitsNet quantity is converted to its scalar value before DPT encoding.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private static object ConvertOutboundValue(object value, DptBase dpt)
    {
        if (TryConvertScalarToDptQuantity(value, dpt, out var quantityValue))
            return quantityValue;

        if (value is UnitsNet.IQuantity quantity
            && TryConvertQuantityToDptScalar(quantity, dpt, out var scalarValue))
            return scalarValue;

        /* not needed any longer because the DPT encoding is done via the DPT instance which already handles UnitsNet quantities and other conversions internally
        if (value is UnitsNet.IQuantity quantity)
            return quantity.Value;
        */

        return value;
    }

    private static bool TryConvertQuantityToDptScalar(UnitsNet.IQuantity quantity, DptBase dpt, out object converted)
    {
        converted = quantity;

        if (typeof(UnitsNet.IQuantity).IsAssignableFrom(dpt.ApplicationType))
            return false;

        if (!IsNumericTargetType(dpt.ApplicationType))
            return false;

        try
        {
            var nonNullable = Nullable.GetUnderlyingType(dpt.ApplicationType) ?? dpt.ApplicationType;
            var magnitude = TryGetDptKnxUnitEnum(dpt, out var knxUnit)
                ? quantity.As(knxUnit)
                : quantity.As(quantity.Unit);

            converted = Convert.ChangeType(magnitude, nonNullable, CultureInfo.InvariantCulture);
            return converted is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void AddValueException(IValue source, string message, Exception? innerException = null)
    {
        if (source is not ValueBase valueBase)
            return;

        var exception = innerException is null
            ? new ValueException(message)
            : new ValueException(message, innerException);
        valueBase.AddException(exception);
    }

    /// <summary>
    /// Normalizes an inbound KNX value to the target IValue type. For example, it converts scalar values to UnitsNet quantities if needed.
    /// </summary>
    /// <param name="targetValue">The target IValue instance.</param>
    /// <param name="decodedValue">The decoded value from KNX.</param>
    /// <returns>The normalized value compatible with the target IValue type.</returns>
    private object? NormalizeInboundValue(IValue? targetValue, object? decodedValue, DptBase? dpt = null)
    {
        // nop, nothing reasonable to do if either the target value or the decoded value is null
        if (targetValue is null || decodedValue is null)
            return decodedValue;

        if (TryAdjustScaledUnitAwareNumeric(decodedValue, targetValue.ValueType, dpt, out var adjustedNumeric))
            return adjustedNumeric;

        // in a normal case, the decoded value is already of the correct type for the target IValue, so we can return it directly
        if (targetValue.ValueType.IsInstanceOfType(decodedValue) || targetValue.ValueType.IsAssignableFrom(decodedValue.GetType()))
            return decodedValue;

        // Log a warning that the decoded value type does not match the target IValue type, but continue with normalization attempts
        _logger.LogDebug("Decoded value type {DecodedType} does not match target IValue type {TargetType} for IValue {ValueName}. Attempting normalization.", decodedValue.GetType().FullName, targetValue.ValueType.FullName, targetValue.Name);

        // Fast path: scalar decoded value for quantity-typed target (for example non-unit-aware DPT mapped to quantity target with Unit metadata).
        if (IsQuantityType(targetValue.ValueType) && IsNumericScalar(decodedValue))
        {
            var numericRaw = decodedValue is IFormattable scalar
                ? scalar.ToString(null, CultureInfo.InvariantCulture)
                : decodedValue.ToString();

            if (!string.IsNullOrWhiteSpace(numericRaw)
                && targetValue.TryParseValue(numericRaw, out var parsedQuantity, out _, CultureInfo.InvariantCulture))
            {
                return parsedQuantity;
            }

            if (double.TryParse(numericRaw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var magnitude)
                && TryCreateDptQuantityInstance(magnitude, dpt, targetValue.ValueType, out var dptQuantity))
            {
                return dptQuantity;
            }
        }

        // Fast path: quantity decoded value for scalar/unit-aware target. Keep bus mapper as transport authority and use value parser for local normalization.
        if (decodedValue is UnitsNet.IQuantity quantityDecoded && !IsQuantityType(targetValue.ValueType))
        {
            if (TryConvertQuantityToNumericTarget(quantityDecoded, targetValue.ValueType, dpt, out var numericValue))
                return numericValue;

            var quantityRaw = quantityDecoded.ToString(CultureInfo.InvariantCulture);
            if (targetValue.TryParseValue(quantityRaw, out var parsedScalarFromQuantity, out _, CultureInfo.InvariantCulture))
                return parsedScalarFromQuantity;
        }

        // Fast path: scalar target with convertible decoded payload (or quantity-like object exposing a Value property).
        if (dpt is not null
            && typeof(UnitsNet.IQuantity).IsAssignableFrom(dpt.ApplicationType)
            && TryConvertDecodedToNumericTarget(decodedValue, targetValue.ValueType, out var decodedNumericTarget))
            return decodedNumericTarget;

        // Can we use the DPT to convert the decoded value to a string for later parsing by IValue?
        if (dpt is not null)
        {
            try
            {
                var groupValue = dpt.ToGroupValue(decodedValue);
                var formatted = dpt.Format(groupValue, CultureInfo.InvariantCulture.TwoLetterISOLanguageName, CultureInfo.InvariantCulture, null);
                if (targetValue.TryParseValue(formatted, out var parsedValueDpt, out _, CultureInfo.InvariantCulture))
                    return parsedValueDpt;
            }
            catch
            {
                // ignore and fall back to the default normalization below
            }
        }

        // Last resort: try to convert the decoded value to a string and parse it using the target IValue's TryParseValue method, which should handle common conversions (e.g., string to int, string to float, etc.)
        var rawText = decodedValue is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : decodedValue.ToString();

        if (string.IsNullOrWhiteSpace(rawText))
            return decodedValue;

        if (targetValue.TryParseValue(rawText, out var parsedValue, out _, CultureInfo.InvariantCulture))
            return parsedValue;

        _logger.LogWarning("Failed to normalize inbound KNX value type {DecodedType} to target type {TargetType} for value {ValueName}. Keeping decoded value as-is.", decodedValue.GetType().FullName, targetValue.ValueType.FullName, targetValue.Name);

        return decodedValue;
    }

    private static bool IsQuantityType(Type type)
        => typeof(UnitsNet.IQuantity).IsAssignableFrom(type);

    private static bool IsNumericTargetType(Type type)
    {
        var nonNullable = Nullable.GetUnderlyingType(type) ?? type;
        return typeof(IConvertible).IsAssignableFrom(nonNullable)
            && nonNullable != typeof(bool)
            && !nonNullable.IsEnum;
    }

    private static bool IsNumericScalar(object value)
        => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static bool TryConvertQuantityToNumericTarget(UnitsNet.IQuantity quantity, Type targetType, DptBase? dpt, out object? converted)
    {
        converted = null;
        if (!IsNumericTargetType(targetType))
            return false;

        try
        {
            var nonNullable = Nullable.GetUnderlyingType(targetType) ?? targetType;
            var numericValue = Convert.ToDouble(quantity.Value, CultureInfo.InvariantCulture);
            if (dpt is DptSimple simple && simple.IsScaledNumeric)
            {
                var coefficient = simple.NumericInfo?.Coefficient ?? 1.0;
                if (!coefficient.Equals(0.0) && !coefficient.Equals(1.0))
                    numericValue /= coefficient;
            }

            converted = Convert.ChangeType(numericValue, nonNullable, CultureInfo.InvariantCulture);
            return converted is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryConvertDecodedToNumericTarget(object decodedValue, Type targetType, out object? converted)
    {
        converted = null;
        if (!IsNumericTargetType(targetType))
            return false;

        var nonNullable = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            if (decodedValue is IConvertible convertible)
            {
                converted = Convert.ChangeType(convertible, nonNullable, CultureInfo.InvariantCulture);
                return converted is not null;
            }

            var valueProperty = decodedValue.GetType().GetProperty("Value");
            if (valueProperty?.GetValue(decodedValue) is IConvertible scalarValue)
            {
                converted = Convert.ChangeType(scalarValue, nonNullable, CultureInfo.InvariantCulture);
                return converted is not null;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool TryConvertScalarToDptQuantity(object value, DptBase dpt, out object converted)
    {
        converted = value;

        if (!IsNumericScalar(value))
            return false;

        if (!TryCreateDptQuantityInstance(Convert.ToDouble(value, CultureInfo.InvariantCulture), dpt, dpt.ApplicationType, out var quantity))
            return false;

        converted = quantity;
        return true;
    }

    private static bool TryCreateDptQuantityInstance(double magnitude, DptBase? dpt, Type expectedType, out object quantity)
    {
        quantity = magnitude;

        if (dpt is null || !typeof(UnitsNet.IQuantity).IsAssignableFrom(dpt.ApplicationType))
            return false;

        if (!TryGetDptKnxUnitEnum(dpt, out var unitEnum))
            return false;

        try
        {
            var created = UnitsNet.Quantity.From(magnitude, unitEnum);
            if (!expectedType.IsInstanceOfType(created) && created.GetType() != expectedType)
                return false;

            quantity = created;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetDptKnxUnitEnum(DptBase dpt, out Enum unitEnum)
    {
        unitEnum = default!;

        var unitProperty = dpt.GetType().GetProperty("KnxUnit");
        if (unitProperty?.GetValue(dpt) is not Enum enumValue)
            return false;

        unitEnum = enumValue;
        return true;
    }

    private static bool TryAdjustScaledUnitAwareNumeric(object decodedValue, Type targetType, DptBase? dpt, out object? adjusted)
    {
        adjusted = null;

        if (dpt is not DptSimple simple || !simple.IsScaledNumeric)
            return false;

        if (!IsNumericTargetType(targetType) || !typeof(UnitsNet.IQuantity).IsAssignableFrom(dpt.ApplicationType))
            return false;

        if (decodedValue is not IConvertible convertible)
            return false;

        var coefficient = simple.NumericInfo?.Coefficient ?? 1.0;
        if (coefficient.Equals(0.0) || coefficient.Equals(1.0))
            return false;

        try
        {
            var nonNullable = Nullable.GetUnderlyingType(targetType) ?? targetType;
            var corrected = convertible.ToDouble(CultureInfo.InvariantCulture) / coefficient;
            adjusted = Convert.ChangeType(corrected, nonNullable, CultureInfo.InvariantCulture);
            return adjusted is not null;
        }
        catch
        {
            return false;
        }
    }
}
