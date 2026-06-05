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
using HomeCompanion.Abstractions;
using HomeCompanion.Persistence;

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
    private readonly IStateInitializationManager stateInitializationManager;
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
        IStateInitializationManager stateInitializationManager,
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
            _logger.LogInformation("Skipping initial read requests for KNX values because communication permissions do not allow receiving read answers or read requests.");
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
        await lifeCycleSync.WaitForInitializationStageCompletedAsync(AppInitializationStage.InitLoadFromStore, TimeSpan.FromSeconds(10), cancellationToken);

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
                        mapping.Value.InitializeValue(e.KnxMessageContext.DecodedValue, AppInitializationStage.InitBusValueReceived);
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

        switch (args.EventType)
        {
            case GroupEventType.ValueWrite:
                if (!_integrationOptions.CommunicationPermissions.HasFlag(CommunicationPermissions.RxGroupAdddressWrites))
                    break;
                if ( !(target?.Mapping.Communication.HasFlag(BusCommunication.Receive) ?? false) )
                    break; // if the mapping doesn't allow receiving, ignore the write
                _ = _publisher.PublishAsync(new KnxGroupWriteReceived
                {
                    DestinationAddress = args.DestinationAddress,
                    SourceAddress = args.SourceAddress,
                    RawValue = args.Value,
                    DecodedValue = ctx.DecodedValue,
                    Value = ctx.DecodedValue,
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
                if ( !(target?.Mapping.Communication.HasFlag(BusCommunication.Receive) ?? false) )
                    break; // if the mapping doesn't allow receiving, ignore the response
                _ = _publisher.PublishAsync(new KnxGroupResponseReceived
                {
                    DestinationAddress = args.DestinationAddress,
                    SourceAddress = args.SourceAddress,
                    RawValue = args.Value,
                    DecodedValue = ctx.DecodedValue,
                    Value = ctx.DecodedValue,
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
                return;
            }
            encoded = dpt.ToGroupValue(request.NewValue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ValueWriteRequest for {GA}: DPT encoding failed, skipping send.", ga);
            return;
        }

        var message = new GroupMessageRequest(ga, encoded, GroupEventType.ValueWrite);
        foreach (var connection in _connections)
        {
            try { await connection.SendMessageAsync(message, cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send KNX write to {GA} on connection {Connection}.", ga, connection); }
        }
    }

}
