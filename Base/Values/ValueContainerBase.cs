using System;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Values;

public abstract class ValueContainerBase(ILogger<ValueContainerBase> logger) : IValuesContainer
{
    protected readonly ILogger<ValueContainerBase> logger = logger;

    public virtual IEnumerable<IValue> GetValues()
    {
        // retrieve all values from this container via reflection by looking for all properties of type IValue
        var valueProperties = GetType().GetProperties()
            .Where(p => typeof(IValue).IsAssignableFrom(p.PropertyType));

        foreach (var property in valueProperties)
        {
            if (property.GetMethod == null)
            {
                logger.LogWarning("Property {PropertyName} of {ContainerType} is of type IValue but has no getter, skipping it", property.Name, GetType().Name);
                continue;
            }
            if (property.GetValue(this) is IValue value)
            {
                yield return value;
            }
        }
    }
}
