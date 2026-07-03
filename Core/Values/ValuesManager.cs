using HomeCompanion.Events;
using HomeCompanion.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Reflection;

namespace HomeCompanion.Values;

/// <summary>
/// Default implementation of <see cref="IValuesManager"/>.
/// Centralizes value initialization and event routing based on the event target field.
/// </summary>
public sealed class ValuesManager : IValuesManager, IHostedService, IDisposable
{
    private readonly IEventPublisher _publisher;
    private readonly IEventSubscriber _subscriber;
    private readonly IEnumerable<IValuesContainer> _containers;
    private readonly IHomeCompanionLifeCycleSynchronization _lifeCycleSynchronization;
    private readonly ILogger<ValuesManager> _logger;
    private readonly ConcurrentDictionary<IValue, bool> _registeredValues = [];
    private readonly ReaderWriterLockSlim _registrationLock = new();
    private long _routedUpdates;
    private long _routedWrites;
    private long _droppedNullTargetUpdates;
    private long _droppedNullTargetWrites;
    private long _droppedUnregisteredUpdates;
    private long _droppedUnregisteredWrites;
    private long _droppedNonReceiverUpdates;
    private long _droppedNonReceiverWrites;
    private long _handlerFailures;
    private bool _disposed;

    public ValuesManager(
        IEventPublisher publisher,
        IEventSubscriber subscriber,
        IEnumerable<IValuesContainer> containers,
        IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization,
        ILogger<ValuesManager> logger)
    {
        _publisher = publisher;
        _subscriber = subscriber;
        _containers = containers;
        _lifeCycleSynchronization = lifeCycleSynchronization;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe once, manager handles centralized routing for all values.
        _subscriber.Subscribe(new ValueUpdateReceivedHandler(this));
        _subscriber.Subscribe(new ValueWriteReceivedHandler(this));

        var discoveredCount = 0;
        var initializedCount = 0;
        foreach (var container in _containers)
        {
            foreach (var value in DiscoverValues(container))
            {
                discoveredCount++;
                value.Initialize(_publisher, this);
                initializedCount++;
            }
        }

        await _lifeCycleSynchronization.SignalInitializationStageCompletedAsync(AppInitializationStage.InitValuesRegistered);

        _logger.LogInformation(
            "Initialized {InitializedCount}/{DiscoveredCount} values across {ContainerCount} containers in ValuesManager and signaled stage {Stage}. Registered={RegisteredCount}.",
            initializedCount,
            discoveredCount,
            _containers.Count(),
            AppInitializationStage.InitValuesRegistered,
            _registeredValues.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ValuesManager route summary: routedUpdates={RoutedUpdates}, routedWrites={RoutedWrites}, droppedNullTargetUpdates={DroppedNullTargetUpdates}, droppedNullTargetWrites={DroppedNullTargetWrites}, droppedUnregisteredUpdates={DroppedUnregisteredUpdates}, droppedUnregisteredWrites={DroppedUnregisteredWrites}, droppedNonReceiverUpdates={DroppedNonReceiverUpdates}, droppedNonReceiverWrites={DroppedNonReceiverWrites}, handlerFailures={HandlerFailures}, registeredValues={RegisteredValues}.",
            _routedUpdates,
            _routedWrites,
            _droppedNullTargetUpdates,
            _droppedNullTargetWrites,
            _droppedUnregisteredUpdates,
            _droppedUnregisteredWrites,
            _droppedNonReceiverUpdates,
            _droppedNonReceiverWrites,
            _handlerFailures,
            _registeredValues.Count);

        return Task.CompletedTask;
    }

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

            receiver.ReceiveWrite(@event.NewValue);
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
}
