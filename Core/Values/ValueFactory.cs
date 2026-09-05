using HomeCompanion.Abstractions;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Values;

public class ValueFactory(
    TimeProvider timeProvider,
    IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization,
    ILoggerFactory loggerFactory,
    ILogger<ValueFactory> logger
) : IValueFactory
{
    public ValueBase CreateValue(Type valueType, string? name = null, string? label = null)
    {
        if (valueType == null)
            throw new ArgumentNullException(nameof(valueType));

        var genericType = typeof(ValueBase<>).MakeGenericType(valueType);
        var instance = Activator.CreateInstance(genericType, name, label, loggerFactory.CreateLogger(genericType), timeProvider) as ValueBase;

        if (instance == null)
            throw new InvalidOperationException($"Could not create an instance of {genericType.FullName} and cast it to {nameof(ValueBase)}.");

        return instance;
    }

    public ValueBase<T> CreateValue<T>(string? name = null, string? label = null) where T : notnull
    {
        var logger = loggerFactory.CreateLogger<ValueBase<T>>();
        return new ValueBase<T>(logger, timeProvider)
        {
            Name = name,
            Label = label
        };
    }

    IValue IValueFactory.CreateValue(Type valueType, string? name, string? label)
    {
        return CreateValue(valueType, name, label);
    }

    IValue IValueFactory.CreateValue<T>(string? name, string? label)
    {
        return CreateValue<T>(name, label);
    }

    public bool TryParseValue(string value, out IValue parsedValueInstance, out string? errorMessage, string? name = null, string? label = null)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        errorMessage = null;

        // Try to infer the type from the string value
        if (bool.TryParse(value, out var boolResult))
        {
            parsedValueInstance = CreateValue<bool>(name, label).WithValue(boolResult, lifeCycleSynchronization.LastCompletedStage);
            return true;
        }

        if (int.TryParse(value, out var intResult))
        {
            parsedValueInstance = CreateValue<int>(name, label).WithValue(intResult, lifeCycleSynchronization.LastCompletedStage);
            return true;
        }

        if (double.TryParse(value, out var doubleResult))
        {
            parsedValueInstance = CreateValue<double>(name, label).WithValue(doubleResult, lifeCycleSynchronization.LastCompletedStage);
            return true;
        }

        if (DateTime.TryParse(value, out var dateTimeResult))
        {
            parsedValueInstance = CreateValue<DateTime>(name, label).WithValue(dateTimeResult, lifeCycleSynchronization.LastCompletedStage);
            return true;
        }

        // If no type could be inferred, return a string value
        parsedValueInstance = CreateValue<string>(name, label).WithValue(value, lifeCycleSynchronization.LastCompletedStage);
        return true;
    }

    public bool TryParseValue<T>(string value, out IValue parsedValueInstance, out string? errorMessage, string? name = null, string? label = null) where T : notnull
    {
        errorMessage = null;
        ValueBase<T> typedValueInstance;

        try
        {
            ArgumentNullException.ThrowIfNull(value, nameof(value));

            typedValueInstance = CreateValue<T>(name, label);
        }
        catch (Exception ex)
        {
            errorMessage = $"Could not create an instance of {typeof(ValueBase<T>).FullName}: {ex.Message}";
            logger.LogWarning(ex, errorMessage);
            parsedValueInstance = null!;
            return false;
        }

        if (typedValueInstance.TryParseValue(value, out var parsedValue, out errorMessage))
        {
            typedValueInstance.WithValue((T)parsedValue!, lifeCycleSynchronization.LastCompletedStage);
            parsedValueInstance = typedValueInstance;
            return true;
        }
        else
        {
            logger.LogWarning("Could not parse the value '{Value}' to type {Type}: {ErrorMessage}", value, typeof(T).FullName, errorMessage);
            parsedValueInstance = typedValueInstance;
            return false;
        }
    }
}