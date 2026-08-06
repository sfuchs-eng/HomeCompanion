using HomeCompanion.Events;
using HomeCompanion.Abstractions;
using HomeCompanion.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Reflection;

namespace HomeCompanion.Values;

/// <summary>
/// Default implementation of <see cref="IValuesManager"/>.
/// Centralizes value initialization and event routing based on the event target field.
/// </summary>
public sealed class ValuesManager : IValuesManager, IHostedService, IDisposable, IDiagnosable
{
    private readonly IEventPublisher _publisher;
    private readonly IEventSubscriber _subscriber;
    private readonly IEnumerable<IValuesContainer> _containers;
    private readonly IHomeCompanionLifeCycleSynchronization _lifeCycleSynchronization;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ValuesManager> _logger;
    private readonly ConcurrentDictionary<IValue, bool> _registeredValues = [];
    private readonly ReaderWriterLockSlim _registrationLock = new();
    private long _routedUpdates;
    private long _routedWrites;
    private long _droppedPreStageUpdates;
    private long _droppedPreStageWrites;
    private long _droppedNullTargetUpdates;
    private long _droppedNullTargetWrites;
    private long _droppedUnregisteredUpdates;
    private long _droppedUnregisteredWrites;
    private long _droppedNonReceiverUpdates;
    private long _droppedNonReceiverWrites;
    private long _handlerFailures;
    private DateTimeOffset? _startRequestedAtUtc;
    private DateTimeOffset? _valuesRegisteredStageCompletedAtUtc;
    private int _lastDiscoveredCount;
    private int _lastInitializedCount;
    private int _lastContainerCount;
    private bool _disposed;

    public string Name => nameof(ValuesManager);

    public ValuesManager(
        IEventPublisher publisher,
        IEventSubscriber subscriber,
        IEnumerable<IValuesContainer> containers,
        IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization,
        TimeProvider timeProvider,
        ILogger<ValuesManager> logger)
    {
        _publisher = publisher;
        _subscriber = subscriber;
        _containers = containers;
        _lifeCycleSynchronization = lifeCycleSynchronization;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _startRequestedAtUtc = _timeProvider.GetUtcNow();

        // Subscribe once, manager handles centralized routing for all values.
        _subscriber.Subscribe(new ValueUpdateReceivedHandler(this));
        _subscriber.Subscribe(new ValueWriteReceivedHandler(this));

        var discoveredCount = 0;
        var initializedCount = 0;
        var containerCount = _containers.Count();
        foreach (var container in _containers)
        {
            foreach (var value in DiscoverValues(container))
            {
                discoveredCount++;
                value.Initialize(_publisher, this);
                initializedCount++;
            }
        }

        _lastContainerCount = containerCount;
        _lastDiscoveredCount = discoveredCount;
        _lastInitializedCount = initializedCount;

        await _lifeCycleSynchronization.SignalInitializationStageCompletedAsync(AppInitializationStage.InitValuesRegistered);
        _valuesRegisteredStageCompletedAtUtc = _timeProvider.GetUtcNow();

        _logger.LogInformation(
            "Initialized {InitializedCount}/{DiscoveredCount} values across {ContainerCount} containers in ValuesManager and signaled stage {Stage}. Registered={RegisteredCount}. StartedAtUtc={StartedAtUtc}, StageCompletedAtUtc={StageCompletedAtUtc}.",
            initializedCount,
            discoveredCount,
            containerCount,
            AppInitializationStage.InitValuesRegistered,
            _registeredValues.Count,
            _startRequestedAtUtc,
            _valuesRegisteredStageCompletedAtUtc);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ValuesManager route summary: routedUpdates={RoutedUpdates}, routedWrites={RoutedWrites}, droppedPreStageUpdates={DroppedPreStageUpdates}, droppedPreStageWrites={DroppedPreStageWrites}, droppedNullTargetUpdates={DroppedNullTargetUpdates}, droppedNullTargetWrites={DroppedNullTargetWrites}, droppedUnregisteredUpdates={DroppedUnregisteredUpdates}, droppedUnregisteredWrites={DroppedUnregisteredWrites}, droppedNonReceiverUpdates={DroppedNonReceiverUpdates}, droppedNonReceiverWrites={DroppedNonReceiverWrites}, handlerFailures={HandlerFailures}, registeredValues={RegisteredValues}, startRequestedAtUtc={StartRequestedAtUtc}, valuesRegisteredStageCompletedAtUtc={ValuesRegisteredStageCompletedAtUtc}.",
            _routedUpdates,
            _routedWrites,
            _droppedPreStageUpdates,
            _droppedPreStageWrites,
            _droppedNullTargetUpdates,
            _droppedNullTargetWrites,
            _droppedUnregisteredUpdates,
            _droppedUnregisteredWrites,
            _droppedNonReceiverUpdates,
            _droppedNonReceiverWrites,
            _handlerFailures,
            _registeredValues.Count,
            _startRequestedAtUtc,
            _valuesRegisteredStageCompletedAtUtc);

        return Task.CompletedTask;
    }

    public Task<IDiagnosticResultNode> GetDiagnosisAsync(CancellationToken cancellationToken)
        => Task.FromResult<IDiagnosticResultNode>(BuildDiagnosticResult());

    /// <inheritdoc/>
    public void RegisterValue(IValue value)
    {
        ThrowIfDisposed();
        _registrationLock.EnterWriteLock();
        try
        {
            _registeredValues.TryAdd(value, true);
            _logger.LogTrace("Registered value {ValueName} ({ValueType}) for event routing.", value.Name, value.ValueType.Name);
        }
        finally
        {
            _registrationLock.ExitWriteLock();
        }
    }

    /// <inheritdoc/>
    public void UnregisterValue(IValue value)
    {
        ThrowIfDisposed();
        _registrationLock.EnterWriteLock();
        try
        {
            _registeredValues.TryRemove(value, out _);
            _logger.LogTrace("Unregistered value {ValueName} ({ValueType}) from event routing.", value.Name, value.ValueType.Name);
        }
        finally
        {
            _registrationLock.ExitWriteLock();
        }
    }

    private void RouteValueUpdateReceived(ValueUpdateReceived @event)
    {
        if (!CanRouteInboundEvent(@event.Target, nameof(ValueUpdateReceived), ref _droppedPreStageUpdates))
            return;

        if (@event.Target is null)
        {
            Interlocked.Increment(ref _droppedNullTargetUpdates);
            _logger.LogTrace("Dropping ValueUpdateReceived event because target is null.");
            return;
        }

        _registrationLock.EnterReadLock();
        try
        {
            if (!_registeredValues.ContainsKey(@event.Target))
            {
                Interlocked.Increment(ref _droppedUnregisteredUpdates);
                _logger.LogTrace("Dropping ValueUpdateReceived for unregistered target {ValueName} ({ValueType}).", @event.Target.Name, @event.Target.ValueType.Name);
                return;
            }

            if (@event.Target is not IValueEventReceiver receiver)
            {
                Interlocked.Increment(ref _droppedNonReceiverUpdates);
                _logger.LogTrace("Dropping ValueUpdateReceived for target {ValueName} ({ValueType}) because it does not implement IValueEventReceiver.", @event.Target.Name, @event.Target.ValueType.Name);
                return;
            }

            receiver.ReceiveUpdate(@event.Value);
            Interlocked.Increment(ref _routedUpdates);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _handlerFailures);
            _logger.LogWarning(ex, "Error routing ValueUpdateReceived event to target {ValueName}.", @event.Target.Name);
        }
        finally
        {
            _registrationLock.ExitReadLock();
        }
    }

    private void RouteValueWriteReceived(ValueWriteReceived @event)
    {
        if (!CanRouteInboundEvent(@event.Target, nameof(ValueWriteReceived), ref _droppedPreStageWrites))
            return;

        if (@event.Target is null)
        {
            Interlocked.Increment(ref _droppedNullTargetWrites);
            _logger.LogTrace("Dropping ValueWriteReceived event because target is null.");
            return;
        }

        _registrationLock.EnterReadLock();
        try
        {
            if (!_registeredValues.ContainsKey(@event.Target))
            {
                Interlocked.Increment(ref _droppedUnregisteredWrites);
                _logger.LogTrace("Dropping ValueWriteReceived for unregistered target {ValueName} ({ValueType}).", @event.Target.Name, @event.Target.ValueType.Name);
                return;
            }

            if (@event.Target is not IValueEventReceiver receiver)
            {
                Interlocked.Increment(ref _droppedNonReceiverWrites);
                _logger.LogTrace("Dropping ValueWriteReceived for target {ValueName} ({ValueType}) because it does not implement IValueEventReceiver.", @event.Target.Name, @event.Target.ValueType.Name);
                return;
            }

            receiver.ReceiveWrite(@event.Value);
            Interlocked.Increment(ref _routedWrites);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _handlerFailures);
            _logger.LogWarning(ex, "Error routing ValueWriteReceived event to target {ValueName}.", @event.Target.Name);
        }
        finally
        {
            _registrationLock.ExitReadLock();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ValuesManager));
    }

    /// <summary>
    /// Checks if the inbound event can be routed based on the current application initialization stage.
    /// If the stage <see cref="AppInitializationStage.InitValuesRegistered"/> is not completed, the event will be dropped and a warning will be logged.
    /// This is to prevent routing events to values that may not have been registered yet, which could lead to lost updates or writes.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="eventName"></param>
    /// <param name="droppedCounter"></param>
    /// <returns></returns>
    private bool CanRouteInboundEvent(IValue? target, string eventName, ref long droppedCounter)
    {
        if (_lifeCycleSynchronization.IsInitializationStageCompleted(AppInitializationStage.InitValuesRegistered))
            return true;

        var dropCount = Interlocked.Increment(ref droppedCounter);
        if (dropCount == 1)
        {
            _logger.LogWarning(
                "Dropping {EventName} before stage {Stage} completed. Target={TargetName}, TargetType={TargetType}, DropCount={DropCount}.",
                eventName,
                AppInitializationStage.InitValuesRegistered,
                target?.Name ?? "<null>",
                target?.ValueType.Name ?? "<unknown>",
                dropCount);
        }
        else
        {
            _logger.LogTrace(
                "Dropping {EventName} before stage {Stage} completed. Target={TargetName}, TargetType={TargetType}, DropCount={DropCount}.",
                eventName,
                AppInitializationStage.InitValuesRegistered,
                target?.Name ?? "<null>",
                target?.ValueType.Name ?? "<unknown>",
                dropCount);
        }

        return false;
    }

    private DiagnosticResultNode BuildDiagnosticResult()
    {
        var snapshot = CaptureSnapshot();
        var root = DiagnosticResultNode.Create(Name);

        root.Records.Add(new DiagnosticRecord("InitValuesRegisteredCompleted", snapshot.InitValuesRegisteredCompleted));
        root.Records.Add(new DiagnosticRecord("StartRequestedAtUtc", snapshot.StartRequestedAtUtc));
        root.Records.Add(new DiagnosticRecord("ValuesRegisteredStageCompletedAtUtc", snapshot.ValuesRegisteredStageCompletedAtUtc));

        var initializationNode = root.AddChild("Initialization");
        initializationNode.Records.Add(new DiagnosticRecord("ContainerCount", snapshot.ContainerCount));
        initializationNode.Records.Add(new DiagnosticRecord("DiscoveredCount", snapshot.DiscoveredCount));
        initializationNode.Records.Add(new DiagnosticRecord("InitializedCount", snapshot.InitializedCount));
        initializationNode.Records.Add(new DiagnosticRecord("RegisteredCount", snapshot.RegisteredCount));

        var routingNode = root.AddChild("Routing");
        routingNode.Records.Add(new DiagnosticRecord("RoutedUpdates", snapshot.RoutedUpdates));
        routingNode.Records.Add(new DiagnosticRecord("RoutedWrites", snapshot.RoutedWrites));
        routingNode.Records.Add(new DiagnosticRecord("DroppedPreStageUpdates", snapshot.DroppedPreStageUpdates));
        routingNode.Records.Add(new DiagnosticRecord("DroppedPreStageWrites", snapshot.DroppedPreStageWrites));
        routingNode.Records.Add(new DiagnosticRecord("DroppedNullTargetUpdates", snapshot.DroppedNullTargetUpdates));
        routingNode.Records.Add(new DiagnosticRecord("DroppedNullTargetWrites", snapshot.DroppedNullTargetWrites));
        routingNode.Records.Add(new DiagnosticRecord("DroppedUnregisteredUpdates", snapshot.DroppedUnregisteredUpdates));
        routingNode.Records.Add(new DiagnosticRecord("DroppedUnregisteredWrites", snapshot.DroppedUnregisteredWrites));
        routingNode.Records.Add(new DiagnosticRecord("DroppedNonReceiverUpdates", snapshot.DroppedNonReceiverUpdates));
        routingNode.Records.Add(new DiagnosticRecord("DroppedNonReceiverWrites", snapshot.DroppedNonReceiverWrites));
        routingNode.Records.Add(new DiagnosticRecord("HandlerFailures", snapshot.HandlerFailures));

        return root;
    }

    private ValuesManagerSnapshot CaptureSnapshot()
        => new(
            _lifeCycleSynchronization.IsInitializationStageCompleted(AppInitializationStage.InitValuesRegistered),
            _startRequestedAtUtc,
            _valuesRegisteredStageCompletedAtUtc,
            _lastContainerCount,
            _lastDiscoveredCount,
            _lastInitializedCount,
            _registeredValues.Count,
            Interlocked.Read(ref _routedUpdates),
            Interlocked.Read(ref _routedWrites),
            Interlocked.Read(ref _droppedPreStageUpdates),
            Interlocked.Read(ref _droppedPreStageWrites),
            Interlocked.Read(ref _droppedNullTargetUpdates),
            Interlocked.Read(ref _droppedNullTargetWrites),
            Interlocked.Read(ref _droppedUnregisteredUpdates),
            Interlocked.Read(ref _droppedUnregisteredWrites),
            Interlocked.Read(ref _droppedNonReceiverUpdates),
            Interlocked.Read(ref _droppedNonReceiverWrites),
            Interlocked.Read(ref _handlerFailures));

    private static IEnumerable<IValue> DiscoverValues(object root)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return DiscoverValuesRecursive(root, visited);
    }

    private static IEnumerable<IValue> DiscoverValuesRecursive(object instance, HashSet<object> visited)
    {
        if (!visited.Add(instance))
            yield break;

        var type = instance.GetType();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                continue;

            object? value;
            try
            {
                value = prop.GetValue(instance);
            }
            catch
            {
                continue;
            }

            if (value is null)
                continue;

            if (value is IValue iValue)
            {
                yield return iValue;
                continue;
            }

            var propType = prop.PropertyType;
            if (propType == typeof(string) || propType.IsPrimitive || propType.IsEnum)
                continue;

            foreach (var nested in DiscoverValuesRecursive(value, visited))
                yield return nested;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _registrationLock?.Dispose();
    }

    // -----

    private sealed class ValueUpdateReceivedHandler(ValuesManager manager) : IEventHandler<ValueUpdateReceived>
    {
        public ValueTask HandleAsync(ValueUpdateReceived @event, CancellationToken cancellationToken = default)
        {
            manager.RouteValueUpdateReceived(@event);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ValueWriteReceivedHandler(ValuesManager manager) : IEventHandler<ValueWriteReceived>
    {
        public ValueTask HandleAsync(ValueWriteReceived @event, CancellationToken cancellationToken = default)
        {
            manager.RouteValueWriteReceived(@event);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record ValuesManagerSnapshot(
        bool InitValuesRegisteredCompleted,
        DateTimeOffset? StartRequestedAtUtc,
        DateTimeOffset? ValuesRegisteredStageCompletedAtUtc,
        int ContainerCount,
        int DiscoveredCount,
        int InitializedCount,
        int RegisteredCount,
        long RoutedUpdates,
        long RoutedWrites,
        long DroppedPreStageUpdates,
        long DroppedPreStageWrites,
        long DroppedNullTargetUpdates,
        long DroppedNullTargetWrites,
        long DroppedUnregisteredUpdates,
        long DroppedUnregisteredWrites,
        long DroppedNonReceiverUpdates,
        long DroppedNonReceiverWrites,
        long HandlerFailures);
}
