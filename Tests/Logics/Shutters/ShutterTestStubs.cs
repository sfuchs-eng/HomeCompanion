using HomeCompanion.Base.Model;
using HomeCompanion.Events;
using HomeCompanion.Persistence;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests.Logics.Shutters;

internal sealed class StubModelProvider : IModelProvider
{
    private readonly Model model;

    public StubModelProvider(Model model)
    {
        this.model = model;
    }

    public bool IsInitialized => true;

    public Model GetModel()
    {
        return model;
    }
}

internal sealed class StubValueReferenceProvider(Dictionary<string, IValue> byReference) : IValueProvider
{
    public void Add(string reference, IValue value) => byReference[reference] = value;

    public IValue Resolve(string reference) => byReference[reference];

    public bool TryResolve(string reference, out IValue? value)
        => byReference.TryGetValue(reference, out value);

    public bool TryResolve<T>(string reference, out IValue<T>? value)
    {
        if (byReference.TryGetValue(reference, out var untyped) && untyped is IValue<T> typed)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }
}

internal class GenerativeReferenceProvider : IValueProvider
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;

    public Dictionary<string, IValue> GeneratedValues { get; }

    public GenerativeReferenceProvider( Dictionary<string, IValue>? generatedValues, ILoggerFactory? loggerFactory, TimeProvider? timeProvider)
    {
        GeneratedValues = generatedValues ?? [];
        _loggerFactory = loggerFactory ?? LoggerFactory.Create(builder => builder.AddProvider(NullLoggerProvider.Instance));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IValue Resolve(string reference)
    {
        if (GeneratedValues.TryGetValue(reference, out var existing))
            return existing;

        IValue val;
        if (reference.StartsWith("Bool:"))
        {
            var valt = new ValueBase<bool>(_loggerFactory.CreateLogger<ValueBase<bool>>(), _timeProvider);
            valt.Write(false);
            val = valt;
        }
        else if (reference.StartsWith("Byte:"))
        {
            var valt = new ValueBase<byte>(_loggerFactory.CreateLogger<ValueBase<byte>>(), _timeProvider);
            valt.Write(0);
            val = valt;
        }
        else if (reference.StartsWith("Int:"))
        {
            var valt = new ValueBase<int>(_loggerFactory.CreateLogger<ValueBase<int>>(), _timeProvider);
            valt.Write(0);
            val = valt;
        }
        else if (reference.StartsWith("Long:"))
        {
            var valt = new ValueBase<long>(_loggerFactory.CreateLogger<ValueBase<long>>(), _timeProvider);
            valt.Write(0L);
            val = valt;
        }
        else if (reference.StartsWith("Float:"))
        {
            var valt = new ValueBase<float>(_loggerFactory.CreateLogger<ValueBase<float>>(), _timeProvider);
            valt.Write(0.0f);
            val = valt;
        }
        else if (reference.StartsWith("Double:"))
        {
            var valt = new ValueBase<double>(_loggerFactory.CreateLogger<ValueBase<double>>(), _timeProvider);
            valt.Write(0.0);
            val = valt;
        }
        else if (reference.StartsWith("String:"))
        {
            var valt = new ValueBase<string>(_loggerFactory.CreateLogger<ValueBase<string>>(), _timeProvider);
            valt.Write("");
            val = valt;
        }
        else
        {
            throw new ArgumentException($"Unknown reference format: {reference}");
        }
        GeneratedValues[reference] = val;
        return val;
    }

    public bool TryResolve(string reference, out IValue? value)
    {
        try
        {
            value = Resolve(reference);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    public bool TryResolve<T>(string reference, out IValue<T>? value)
    {
        if (TryResolve(reference, out var untyped) && untyped is IValue<T> typed)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }
}

internal sealed class StubStateStore() : IStateStore
{
    public Task<StateLoadingResult<T>> LoadAsync<T>(string stateSetName, TimeSpan maxAge) where T : class, new()
    {
        T state;
        /*
        if (typeof(T) == typeof(ShutterManualOverrideStateSet) && _preloadedState is not null)
            state = (T)(object)_preloadedState;
        else
            state = new T();
            */
        state = new T();

        return Task.FromResult(new StateLoadingResult<T>
        {
            IsSuccess = true,
            IsRecent = true,
            StateSet = state,
        });
    }

    public Task<StateLoadingResult<T>> LoadAsync<T>(string stateSetName) where T : class, new()
        => LoadAsync<T>(stateSetName, TimeSpan.FromMinutes(30));

    public Task SaveAsync<T>(string stateSetName, T stateSet, CancellationToken cancellation) where T : class, new()
    {
        /*
        if (stateSet is ShutterManualOverrideStateSet typed)
            Stored = typed;
        */
        return Task.CompletedTask;
    }

    public Task SaveAsync<T>(string stateSetName, T stateSet, int timeoutSeconds = 30) where T : class, new()
    {
        /*
        if (stateSet is ShutterManualOverrideStateSet typed)
            Stored = typed;
        */
        return Task.CompletedTask;
    }
}

internal sealed class StubPublisher : IEventPublisher
{
    public ValueTask PublishAsync(IEvent @event, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

internal sealed class StubSubscriber : IEventSubscriber
{
    private readonly Dictionary<Type, List<object>> _handlersByType = [];

    public void Subscribe<T>(IEventHandler<T> handler) where T : IEvent
    {
        if (!_handlersByType.TryGetValue(typeof(T), out var handlers))
        {
            handlers = [];
            _handlersByType[typeof(T)] = handlers;
        }

        handlers.Add(handler);
    }

    public async Task PublishAsync<T>(T @event) where T : IEvent
    {
        if (!_handlersByType.TryGetValue(typeof(T), out var handlers))
            return;

        foreach (var handler in handlers.Cast<IEventHandler<T>>())
            await handler.HandleAsync(@event);
    }

    public void Subscribe<T>(EventHandlerDelegate<T> handler) where T : IEvent
    {
        throw new NotImplementedException();
    }
}
