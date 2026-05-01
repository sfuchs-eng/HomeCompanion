using HomeCompanion.Abstractions;
using HomeCompanion.Base.Events;
using HomeCompanion.Base.Values;
using HomeCompanion.Knx;
using HomeCompanion.Knx.Events;
using Microsoft.Extensions.Logging;
using SRF.Knx.Core;
using SRF.Network.Knx;
using SRF.Network.Knx.Messages;
using System.Collections.Concurrent;
using System.Reflection;

namespace HomeCompanion.Core.BusConnectors.Knx;

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

        // Send GroupValueRead for each registered GA to seed initial values from bus
        if (_valueMap.Count > 0)
            _ = SendInitialReadRequestsAsync(cancellationToken);
        else
            _isInitializationFinished = true;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
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
    // Initial read requests
    // -------------------------------------------------------------------------

    private async Task SendInitialReadRequestsAsync(CancellationToken cancellationToken)
    {
        foreach (var ga in _valueMap.Keys)
            _pendingInitialReads.TryAdd(ga, true);

        foreach (var ga in _valueMap.Keys)
        {
            var readRequest = new GroupMessageRequest(ga, new SRF.Knx.Core.GroupValue(), GroupEventType.ValueRead);
            foreach (var connection in _connections)
            {
                try { await connection.SendMessageAsync(readRequest, cancellationToken); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to send initial read request for {GA}.", ga); }
            }
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

        _isInitializationFinished = true;
    }

    // -------------------------------------------------------------------------
    // Inbound: KNX → EventBus
    // -------------------------------------------------------------------------

    private void OnMessageReceived(object? sender, KnxMessageReceivedEventArgs e)
    {
        var ctx = e.KnxMessageContext;
        if (ctx.GroupEventArgs is not { } args) return;

        // Decode the value if the connection layer didn't already do it.
        if (ctx.DecodedValue is null)
        {
            try
            {
                var dpt = _dptResolver.GetDpt(args.DestinationAddress);
                ctx.Dpt = dpt;
                ctx.DecodedValue = dpt.ToValue(args.Value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "KnxConnectivityProvider: DPT decode failed for {GA}.", args.DestinationAddress);
            }
        }

        switch (args.EventType)
        {
            case GroupEventType.ValueWrite:
                _ = _publisher.PublishAsync(new KnxGroupWriteReceived
                {
                    DestinationAddress = args.DestinationAddress,
                    SourceAddress      = args.SourceAddress,
                    RawValue           = args.Value,
                    DecodedValue       = ctx.DecodedValue,
                    ReceivedAt         = ctx.ReceivedAt,
                });
                PublishValueWriteReceived(args.DestinationAddress, ctx.DecodedValue);
                break;

            case GroupEventType.ValueRead:
                _ = _publisher.PublishAsync(new KnxGroupReadReceived
                {
                    DestinationAddress = args.DestinationAddress,
                    SourceAddress      = args.SourceAddress,
                    ReceivedAt         = ctx.ReceivedAt,
                });
                PublishValueReadReceived(args.DestinationAddress);
                break;

            case GroupEventType.ValueResponse:
                _ = _publisher.PublishAsync(new KnxGroupResponseReceived
                {
                    DestinationAddress = args.DestinationAddress,
                    SourceAddress      = args.SourceAddress,
                    RawValue           = args.Value,
                    DecodedValue       = ctx.DecodedValue,
                    ReceivedAt         = ctx.ReceivedAt,
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

