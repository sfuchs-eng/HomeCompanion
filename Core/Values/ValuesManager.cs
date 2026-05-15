using HomeCompanion.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Reflection;

namespace HomeCompanion.Values;

/// <summary>
/// Default implementation of <see cref="IValuesManager"/>.
/// Centralizes value initialization and event routing based on the event target field.
/// </summary>
internal sealed class ValuesManager : IValuesManager, IHostedService, IDisposable
{
    private readonly IEventPublisher _publisher;
    private readonly IEventSubscriber _subscriber;
    private readonly IEnumerable<IValuesContainer> _containers;
    private readonly ILogger<ValuesManager> _logger;
    private readonly ConcurrentDictionary<IValue, bool> _registeredValues = [];
    private readonly ReaderWriterLockSlim _registrationLock = new();
    private bool _disposed;

    public ValuesManager(
        IEventPublisher publisher,
        IEventSubscriber subscriber,
        IEnumerable<IValuesContainer> containers,
        ILogger<ValuesManager> logger)
    {
        _publisher = publisher;
        _subscriber = subscriber;
        _containers = containers;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe once, manager handles centralized routing for all values.
        _subscriber.Subscribe(new ValueUpdateReceivedHandler(this));
        _subscriber.Subscribe(new ValueWriteReceivedHandler(this));

        var initializedCount = 0;
        foreach (var container in _containers)
        {
            foreach (var value in DiscoverValues(container))
            {
                value.Initialize(_publisher, this);
                initializedCount++;
            }
        }

        _logger.LogInformation("Initialized {Count} values across {ContainerCount} containers in ValuesManager.", initializedCount, _containers.Count());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
        if (@event.Target is null) return; // No target, no routing needed

        _registrationLock.EnterReadLock();
        try
        {
            // Check if this value is registered
            if (_registeredValues.ContainsKey(@event.Target) && @event.Target is IValueEventReceiver receiver)
                receiver.ReceiveUpdate(@event.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error routing ValueUpdateReceived event to target {ValueName}.", @event.Target.Name);
        }
        finally
        {
            _registrationLock.ExitReadLock();
        }
    }

    private void RouteValueWriteReceived(ValueWriteReceived @event)
    {
        if (@event.Target is null) return; // No target, no routing needed

        _registrationLock.EnterReadLock();
        try
        {
            // Check if this value is registered
            if (_registeredValues.ContainsKey(@event.Target) && @event.Target is IValueEventReceiver receiver)
                receiver.ReceiveWrite(@event.NewValue);
        }
        catch (Exception ex)
        {
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
