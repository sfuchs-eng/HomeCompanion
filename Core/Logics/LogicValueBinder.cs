using HomeCompanion.Logics;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace HomeCompanion.Core.Logics;

internal sealed class LogicValueBinder(
    IValueProvider valueReferenceProvider,
    IOptions<CoreOptions> options,
    ILogger<LogicValueBinder> logger)
{
    private readonly IValueProvider _valueReferenceProvider = valueReferenceProvider;
    private readonly CoreOptions _options = options.Value;
    private readonly ILogger<LogicValueBinder> _logger = logger;

    public void Bind(ILogic logic)
    {
        var logicType = logic.GetType();
        var properties = logicType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => typeof(IValue).IsAssignableFrom(p.PropertyType));

        foreach (var property in properties)
        {
            var attribute = property.GetCustomAttribute<ValueBindingAttribute>();
            var hasConfig = TryGetConfiguredReference(logicType, property.Name, out var configuredReference);

            if (attribute is null && !hasConfig)
                continue;

            if (property.SetMethod is null)
            {
                throw new InvalidOperationException(
                    $"Logic value binding requires a writable property, but '{logicType.FullName}.{property.Name}' is read-only.");
            }

            string reference;
            if (hasConfig)
            {
                reference = configuredReference!;
                if (attribute is not null)
                {
                    _logger.LogWarning(
                        "Config binding overrides attribute binding for {LogicType}.{PropertyName}. AttributeReference={AttributeReference}, ConfigReference={ConfigReference}.",
                        logicType.FullName,
                        property.Name,
                        attribute.Reference,
                        configuredReference);
                }
            }
            else
            {
                reference = attribute!.Reference;
            }

            var value = _valueReferenceProvider.Resolve(reference);
            if (!property.PropertyType.IsAssignableFrom(value.GetType()))
            {
                throw new InvalidOperationException(
                    $"Binding '{logicType.FullName}.{property.Name}' with reference '{reference}' resolved value type '{value.GetType().Name}', which is not assignable to property type '{property.PropertyType.Name}'.");
            }

            property.SetValue(logic, value);
        }
    }

    private bool TryGetConfiguredReference(Type logicType, string propertyName, out string? reference)
    {
        reference = null;

        if (_options.LogicValueBindings.Count == 0)
            return false;

        var keys = new List<string>
        {
            $"{logicType.Name}.{propertyName}",
        };

        if (!string.IsNullOrWhiteSpace(logicType.FullName))
            keys.Insert(0, $"{logicType.FullName}.{propertyName}");

        foreach (var key in keys)
        {
            if (!_options.LogicValueBindings.TryGetValue(key, out var candidate) || string.IsNullOrWhiteSpace(candidate))
                continue;

            reference = candidate;
            return true;
        }

        return false;
    }
}
