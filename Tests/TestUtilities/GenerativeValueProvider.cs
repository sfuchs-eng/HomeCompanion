using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests.TestUtilities;

/// <summary>
/// Creates IValue instances on demand for the requested reference, and returns the same instance for subsequent requests for the same reference.
/// The reference must be prefixed with the type, e.g. "Bool:MyBoolValue", "Int:MyIntValue", "String:MyStringValue", etc. The value will be initialized to the default value for the type.
/// </summary>
internal class GenerativeValueProvider : IValueProvider
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;

    public Dictionary<string, IValue> GeneratedValues { get; }

    public GenerativeValueProvider(Dictionary<string, IValue>? generatedValues, ILoggerFactory? loggerFactory, TimeProvider? timeProvider)
    {
        GeneratedValues = generatedValues ?? new Dictionary<string, IValue>();
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
            var valt = new ValueBase<bool>(_loggerFactory.CreateLogger<ValueBase<bool>>(), _timeProvider) { Name = reference };
            valt.Write(false);
            val = valt;
        }
        else if (reference.StartsWith("Byte:"))
        {
            var valt = new ValueBase<byte>(_loggerFactory.CreateLogger<ValueBase<byte>>(), _timeProvider) { Name = reference };
            valt.Write(0);
            val = valt;
        }
        else if (reference.StartsWith("Int:"))
        {
            var valt = new ValueBase<int>(_loggerFactory.CreateLogger<ValueBase<int>>(), _timeProvider) { Name = reference };
            valt.Write(0);
            val = valt;
        }
        else if (reference.StartsWith("Long:"))
        {
            var valt = new ValueBase<long>(_loggerFactory.CreateLogger<ValueBase<long>>(), _timeProvider) { Name = reference };
            valt.Write(0L);
            val = valt;
        }
        else if (reference.StartsWith("Float:"))
        {
            var valt = new ValueBase<float>(_loggerFactory.CreateLogger<ValueBase<float>>(), _timeProvider) { Name = reference };
            valt.Write(0.0f);
            val = valt;
        }
        else if (reference.StartsWith("Double:"))
        {
            var valt = new ValueBase<double>(_loggerFactory.CreateLogger<ValueBase<double>>(), _timeProvider) { Name = reference };
            valt.Write(0.0);
            val = valt;
        }
        else if (reference.StartsWith("String:"))
        {
            var valt = new ValueBase<string>(_loggerFactory.CreateLogger<ValueBase<string>>(), _timeProvider) { Name = reference };
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
