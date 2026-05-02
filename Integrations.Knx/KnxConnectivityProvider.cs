using HomeCompanion.Abstractions;
using HomeCompanion.Base.Events;
using HomeCompanion.Base.Values;
using HomeCompanion.Integrations.Knx.Events;
using Microsoft.Extensions.Logging;
using SRF.Knx.Core;
using SRF.Network.Knx;
using SRF.Network.Knx.Messages;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace HomeCompanion.Integrations.Knx;

/// <summary>
/// KNX connectivity provider. Bridges one or more <see cref="IKnxConnection"/> instances to the
/// HomeCompanion event bus.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inbound</b> (KNX → EventBus): On each received telegram, the corresponding HC event is published:
/// <list type="bullet">
///   <item><c>GroupValueWrite</c> → <see cref="KnxGroupWriteReceived"/> + <see cref="ValueWriteReceived"/></item>
///   <item><c>GroupValueRead</c> → <see cref="KnxGroupReadReceived"/> + <see cref="ValueReadReceived"/></item>
///   <item><c>GroupValueResponse</c> → <see cref="KnxGroupResponseReceived"/> + <see cref="ValueReadAnswerReceived"/> + <see cref="ValueWriteReceived"/></item>
/// </list>
/// </para>
/// <para>
/// <b>Outbound</b> (EventBus → KNX): Subscribes to <see cref="ValueWritten"/> on the HC event bus.
/// When the source value has a <see cref="KnxBusEndpointMapping"/>, the value is encoded and broadcast
/// as a <c>GroupValueWrite</c> telegram to all registered connections.
/// </para>
/// <para>
/// <b>Value discovery</b>: At startup, all <see cref="IValue"/> properties (any visibility, any depth) on
/// registered <see cref="IValuesContainer"/> instances that carry a <see cref="KnxBusEndpointMapping"/> are
/// discovered via reflection. Each is initialized with <see cref="IValue.Initialize"/> and indexed by its
/// <see cref="KnxBusEndpointMapping.GroupAddress"/>. A <c>GroupValueRead</c> is sent for each registered
/// group address so that the bus can respond with the current value, completing initial value population.
/// </para>
/// </remarks>
public sealed class KnxConnectivityProvider : IConnectivityProvider
{
    private static readonly TimeSpan InitializationReadTimeout = TimeSpan.FromSeconds(30);

    private readonly IReadOnlyList<IKnxConnection> _connections;
    private readonly IEventPublisher _publisher;
    private readonly IEventSubscriber _subscriber;
    private readonly IReadOnlyList<IValuesContainer> _containers;
    private readonly IDptResolver _dptResolver;
    private readonly ILogger<KnxConnectivityProvider> _logger;

    /// <summary>Group address → registered value map, built at startup.</summary>
    private Dictionary<GroupAddress, IValue> _valueMap = [];

    /// <summary>Tracks which group addresses still need an initial read response.</summary>
    private readonly ConcurrentDictionary<GroupAddress, bool> _pendingInitialReads = [];

    private volatile bool _isInitializationFinished;

    /// <inheritdoc/>
    public bool IsConnected => _connections.Any(c => c.IsConnected);

    private Task ValueInitializationTask = Task.CompletedTask; // placeholder task to track when initial value population is finished, so that ValueReadReceived events can await it to ensure values are populated before responding to read requests
    private CancellationTokenSource? ValueInitializationCts; // separate CTS to allow canceling the initialization wait if needed, e.g. on shutdown

    /// <inheritdoc/>
    public bool IsInitializationFinished => _isInitializationFinished;

    /// <summary>
    /// Initializes a new <see cref="KnxConnectivityProvider"/>.
    /// </summary>
    public KnxConnectivityProvider(
        IEnumerable<IKnxConnection> connections,
        IEventPublisher publisher,
        IEventSubscriber subscriber,
        IEnumerable<IValuesContainer> containers,
        IDptResolver dptResolver,
        ILogger<KnxConnectivityProvider> logger)
    {
        _connections = connections.ToList();
        _publisher = publisher;
        _subscriber = subscriber;
        _containers = containers.ToList();
        _dptResolver = dptResolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe to outbound write requests from the event bus
        _subscriber.Subscribe(new ValueWrittenHandler(this));

        // Discover and initialize all IValue properties with a KNX bus mapping
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
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // in case the initialization is still running, cancel it to avoid waiting for missing read responses during shutdown
        ValueInitializationCts?.Cancel();
        try { await ValueInitializationTask; }
        catch (OperationCanceledException) { /* expected on cancellation, ignore */ }

        foreach (var connection in _connections)
        {
            connection.MessageReceived -= OnMessageReceived;
            connection.ConnectionStatusChanged -= OnConnectionStatusChanged;
            await connection.DisconnectAsync(cancellationToken);
        }
    }

    // -------------------------------------------------------------------------
    // Value discovery
    // -------------------------------------------------------------------------

    private Dictionary<GroupAddress, IValue> DiscoverKnxValues()
    {
        var map = new Dictionary<GroupAddress, IValue>();
        foreach (var container in _containers)
            DiscoverKnxValuesIn(container, container.GetType(), map);
        return map;
    }

    private void DiscoverKnxValuesIn(object instance, Type type, Dictionary<GroupAddress, IValue> map)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            if (!prop.CanRead) continue;
            if (prop.GetIndexParameters().Length > 0) continue;

            if (prop.GetValue(instance) is not IValue value) continue;
            if (!value.TryGetBusEndpoint<KnxBusEndpointMapping>(KnxBusEndpointMapping.BusId, out var mapping)) continue;

            value.Initialize(_publisher, _subscriber);

            if (map.TryGetValue(mapping!.GroupAddress, out _))
                _logger.LogWarning(
                    "Duplicate KNX group address {GA} found on property '{Prop}' of {Type}; already registered by another value. Skipping.",
                    mapping.GroupAddress, prop.Name, type.FullName);
            else
                map[mapping.GroupAddress] = value;
        }
    }

    // -------------------------------------------------------------------------
    // Initial read requests, typically a background task at startup to populate values with current bus state
    // -------------------------------------------------------------------------

    private async Task SendInitialReadRequestsAndMonitorAsync(CancellationToken cancellationToken)
    {
        // wait until all connections are established before sending initial read requests
        while (!_connections.All(c => c.IsConnected))
            await Task.Delay(200, cancellationToken);
            
        foreach (var ga in _valueMap.Keys)
            _pendingInitialReads.TryAdd(ga, true);

        // register answer handlers before sending read requests
        foreach (var connection in _connections)
            connection.MessageReceived += ProcessInitialReadResponse;

        // send read requests with some delay in between to avoid overwhelming the bus at startup, especially if there are many group addresses to read
        foreach (var ga in _valueMap.Keys)
        {
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
        // Whatever sends backa value suitable for initializing we consider accordingly. Means Write and ReadAnswer telegrams, but not Read requests.
        switch (e.KnxMessageContext.GroupEventArgs?.EventType)
        {
            case GroupEventType.ValueWrite:
            case GroupEventType.ValueResponse:
                if (e.KnxMessageContext.GroupEventArgs.DestinationAddress is { } ga)
                    _pendingInitialReads.TryRemove(ga, out _);
                break;
        }
    }


    // -------------------------------------------------------------------------
    // Inbound: KNX → EventBus
    // -------------------------------------------------------------------------

    private void OnMessageReceived(object? sender, KnxMessageReceivedEventArgs e)
    {
        var ctx = e.KnxMessageContext;
        if (ctx.GroupEventArgs is not { } args) return;

        // if there's a payload that can be decoded, the KNX connection layer has decoded it already.
        // So failure handling onnly needs to cover for unlikely cases, where there should be a payload but either it's missing or invalid, resulting in no deccoded value.

        switch (args.EventType)
        {
            case GroupEventType.ValueWrite:
                // need payload, but event can still be published even if missing/invalid, so listeners at least know that a write was attempted (even if they don't have the new value).
                _ = _publisher.PublishAsync(new KnxGroupWriteReceived
                {
                    DestinationAddress = args.DestinationAddress,
                    SourceAddress = args.SourceAddress,
                    RawValue = args.Value,
                    DecodedValue = ctx.DecodedValue,
                    ReceivedAt = ctx.ReceivedAt,
                });
                PublishValueWriteReceived(args.DestinationAddress, ctx.DecodedValue);
                break;

            case GroupEventType.ValueRead:
                // that's a read request, so no payload expeccted. Just publish the event and trigger a ValueReadReceived so that any listeners can respond with the current value, which should prompt the bus to send a ValueResponse with the current value.
                _ = _publisher.PublishAsync(new KnxGroupReadReceived
                {
                    DestinationAddress = args.DestinationAddress,
                    SourceAddress = args.SourceAddress,
                    ReceivedAt = ctx.ReceivedAt,
                });
                PublishValueReadReceived(args.DestinationAddress);
                break;

            case GroupEventType.ValueResponse:
                // need payload, but even if missing/invalid, we can still publish the event so listeners at least know that a response was received (even if they don't have the current value).
                _ = _publisher.PublishAsync(new KnxGroupResponseReceived
                {
                    DestinationAddress = args.DestinationAddress,
                    SourceAddress = args.SourceAddress,
                    RawValue = args.Value,
                    DecodedValue = ctx.DecodedValue,
                    ReceivedAt = ctx.ReceivedAt,
                });
                PublishValueReadAnswerReceived(args.DestinationAddress, ctx.DecodedValue);
                // A read response also carries the current value — publish as write so the value updates.
                PublishValueWriteReceived(args.DestinationAddress, ctx.DecodedValue);
                _pendingInitialReads.TryRemove(args.DestinationAddress, out _);
                break;
        }
    }

    private void PublishValueWriteReceived(GroupAddress ga, object? decodedValue)
    {
        if (_valueMap.TryGetValue(ga, out var value))
            _ = _publisher.PublishAsync(new ValueWriteReceived { Target = value, NewValue = decodedValue });
    }

    private void PublishValueReadReceived(GroupAddress ga)
    {
        if (_valueMap.TryGetValue(ga, out var value))
            _ = _publisher.PublishAsync(new ValueReadReceived { Target = value });
    }

    private void PublishValueReadAnswerReceived(GroupAddress ga, object? decodedValue)
    {
        if (_valueMap.TryGetValue(ga, out var value))
            _ = _publisher.PublishAsync(new ValueReadAnswerReceived { Target = value, Value = decodedValue });
    }

    private void OnConnectionStatusChanged(object? sender, KnxConnectionEventArgs e)
    {
        _logger.LogInformation("KNX connection status changed. IsConnected={IsConnected}", IsConnected);
    }

    // -------------------------------------------------------------------------
    // Outbound: EventBus → KNX
    // -------------------------------------------------------------------------

    private async Task HandleValueWrittenAsync(ValueWritten written, CancellationToken cancellationToken)
    {
        if (!written.Source.TryGetBusEndpoint<KnxBusEndpointMapping>(KnxBusEndpointMapping.BusId, out var mapping))
            return; // not a KNX-backed value

        var ga = mapping!.GroupAddress;

        SRF.Knx.Core.GroupValue encoded;
        try
        {
            var dpt = _dptResolver.GetDpt(ga);
            if (written.Value is null)
            {
                _logger.LogWarning("ValueWritten for {GA}: value is null, skipping send.", ga);
                return;
            }
            encoded = dpt.ToGroupValue(written.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ValueWritten for {GA}: DPT encoding failed, skipping send.", ga);
            return;
        }

        var message = new GroupMessageRequest(ga, encoded, GroupEventType.ValueWrite);
        foreach (var connection in _connections)
        {
            try { await connection.SendMessageAsync(message, cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send KNX write to {GA} on connection {Connection}.", ga, connection); }
        }
    }

    // -------------------------------------------------------------------------
    // Inner event handler
    // -------------------------------------------------------------------------

    private sealed class ValueWrittenHandler : IEventHandler<ValueWritten>
    {
        private readonly KnxConnectivityProvider _provider;
        public ValueWrittenHandler(KnxConnectivityProvider provider) => _provider = provider;
        public ValueTask HandleAsync(ValueWritten @event, CancellationToken cancellationToken = default)
            => new(_provider.HandleValueWrittenAsync(@event, cancellationToken));
    }
}
