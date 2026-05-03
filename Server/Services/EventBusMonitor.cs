using HomeCompanion.Abstractions;
using HomeCompanion.Base.Events;
using HomeCompanion.Base.Values;

namespace HomeCompanion.Server.Services;

/// <summary>Immutable snapshot of a single event bus entry captured for display.</summary>
public sealed record EventEntry(DateTimeOffset Timestamp, string EventType, string Details);

/// <summary>
/// Singleton service that subscribes to every concrete <see cref="IEvent"/> type found in the loaded assemblies
/// and forwards events to any currently-registered listeners (i.e. open browser sessions).
/// </summary>
/// <remarks>
/// No internal buffer is kept. When no browser session has the Event Monitor page open the listener list
/// is empty and the forwarding cost is essentially zero.
/// </remarks>
public sealed class EventBusMonitor
{
    private readonly TimeProvider _timeProvider;
    private readonly Lock _lock = new();
    private readonly List<Action<EventEntry>> _listeners = [];

    /// <summary>
    /// Initialises the monitor and subscribes to all concrete <see cref="IEvent"/> implementations found in
    /// every currently-loaded assembly.
    /// </summary>
    public EventBusMonitor(IEventSubscriber subscriber, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        SubscribeToAllEventTypes(subscriber);
    }

    /// <summary>
    /// Registers a listener that is called for every event dispatched on the bus while the registration is live.
    /// Dispose the returned handle to unregister.
    /// </summary>
    public IDisposable Register(Action<EventEntry> listener)
    {
        lock (_lock)
            _listeners.Add(listener);

        return new Registration(this, listener);
    }

    internal void Dispatch(IEvent evt)
    {
        var entry = new EventEntry(
            _timeProvider.GetLocalNow(),
            GetEventTypeName(evt.GetType()),
            FormatDetails(evt));

        Action<EventEntry>[] snapshot;
        lock (_lock)
        {
            if (_listeners.Count == 0) return;
            snapshot = [.. _listeners];
        }

        foreach (var listener in snapshot)
            listener(entry);
    }

    private void Unregister(Action<EventEntry> listener)
    {
        lock (_lock)
            _listeners.Remove(listener);
    }

    // ---- Subscription wiring -----------------------------------------------

    private void SubscribeToAllEventTypes(IEventSubscriber subscriber)
    {
        subscriber.Subscribe(new EventBusMonitorHandler<HomeCompanionEvent>(this));
        /*
        var iEventType = typeof(IEvent);
        var subscribeMethod = typeof(IEventSubscriber).GetMethod(nameof(IEventSubscriber.Subscribe))
            ?? throw new InvalidOperationException("IEventSubscriber.Subscribe method not found.");

        var handlerOpenType = typeof(EventBusMonitorHandler<>);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            IEnumerable<Type> types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null)!; }

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                    continue;
                if (!iEventType.IsAssignableFrom(type))
                    continue;

                var handlerType = handlerOpenType.MakeGenericType(type);
                var handler = Activator.CreateInstance(handlerType, this)!;
                var concreteSubscribe = subscribeMethod.MakeGenericMethod(type);
                concreteSubscribe.Invoke(subscriber, [handler]);
            }
        }
        */
    }

    // ---- Formatting ---------------------------------------------------------

    private static string GetEventTypeName(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        var baseName = type.Name[..type.Name.IndexOf('`')];
        var args = type.GetGenericArguments();
        return $"{baseName}<{string.Join(", ", args.Select(a => a.Name))}>";
    }

    private static string FormatDetails(IEvent evt) => evt switch
    {
        ValueChanged vc => FormatValueChanged(vc),
        ValueWritten vw => $"{vw.Source.Name ?? vw.Source.GetType().Name} = {vw.Value}",
        ValueWriteRequest vwr => $"→ {vwr.Source.Name ?? vwr.Source.GetType().Name} = {vwr.NewValue}",
        ValueWriteReceived vwrc => $"← {vwrc.Target?.Name ?? vwrc.Target?.GetType().Name ?? "?"} = {vwrc.NewValue}",
        ValueReadReceived vrr => $"? {vrr.Target?.Name ?? vrr.Target?.GetType().Name ?? "?"}",
        ValueReadAnswerReceived vrar => $"? {vrar.Target?.Name ?? vrar.Target?.GetType().Name ?? "?"} ← {vrar.Value}",
        _ => evt.ToString() ?? evt.GetType().Name
    };

    private static string FormatValueChanged(ValueChanged vc)
    {
        // ValueChanged<T> exposes Source, OldValue, NewValue via reflection (generic type params vary)
        var type = vc.GetType();
        var sourceProp = type.GetProperty("Source");
        var oldProp = type.GetProperty("OldValue");
        var newProp = type.GetProperty("NewValue");

        if (sourceProp is null || oldProp is null || newProp is null)
            return vc.ToString() ?? "ValueChanged";

        var source = sourceProp.GetValue(vc) as IValue;
        var name = source?.Name ?? source?.GetType().Name ?? "?";
        return $"{name}: {oldProp.GetValue(vc)} → {newProp.GetValue(vc)}";
    }

    // ---- Inner types --------------------------------------------------------

    private sealed class EventBusMonitorHandler<T>(EventBusMonitor monitor) : IEventHandler<T>
        where T : IEvent
    {
        public ValueTask HandleAsync(T @event, CancellationToken cancellationToken = default)
        {
            monitor.Dispatch(@event);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Registration(EventBusMonitor monitor, Action<EventEntry> listener) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            monitor.Unregister(listener);
        }
    }
}
