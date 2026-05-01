using System;

namespace HomeCompanion.Base.Values;

public abstract class ValueContainerBase : IValuesContainer
{
    public virtual IEnumerable<IValue> GetValues()
    {
        // retrieve all values from this container via reflection by looking for all properties of type IValue
        var valueProperties = GetType().GetProperties()
            .Where(p => typeof(IValue).IsAssignableFrom(p.PropertyType));

        foreach (var property in valueProperties)
        {
            if (property.GetValue(this) is IValue value)
            {
                yield return value;
            }
        }
    }
}
