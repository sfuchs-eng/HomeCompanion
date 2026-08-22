using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace HomeCompanion.Values;

// for IValue, implement IParameter straight into IValue and BaseValue, and implement IParameter in ValueBase<T> for the generic version. This allows logics to expose parameters that can be configured in the Web UI, and also allows mapping IValues to IParameters for writing to IValues from the Web UI.
// for basic types (bool, int, float, string), implement IParameter in a Parameter<T> class here that can be used to expose parameters of those types.

public class Parameter<T> : IParameter where T : notnull, IEquatable<T>, IFormattable, IParsable<T>
{
    public Parameter(string label, string? name = null)
    {
        Label = label;
        Name = name ?? label;
    }

    public IEqualityComparer<T> Comparer { get; init; } = EqualityComparer<T>.Default;

    public string Name { get; protected set; }

    private T _value = default!;
    public virtual T Value
    {
        get => _value;
        set
        {
            var oldValue = _value;
            _value = value;
            if (!Comparer.Equals(oldValue, value))
            {
                OnChanged();
            }
        }
    }

    public string Label { get; protected set; }

    public string? Description { get; init; }

    public string FormatValue()
    {
        return Value.ToString(null, CultureInfo.InvariantCulture);
    }

    public bool SetValueFromString(string value, out string? errorMessage)
    {
        try
        {
            Value = T.Parse(value, CultureInfo.InvariantCulture);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private void OnChanged()
    {
        foreach (var callback in _changedCallbacks)
        {
            callback(this);
        }
    }

    private List<Action<IParameter>> _changedCallbacks = new List<Action<IParameter>>();

    public IParameterCallbackRegistration RegisterChangedCallback(Action<IParameter> callback)
    {
        _changedCallbacks.Add(callback);
        return new ParameterCallbackRegistration(() => _changedCallbacks.Remove(callback));
    }

    private sealed class ParameterCallbackRegistration : IParameterCallbackRegistration
    {
        private readonly Action _unregister;

        public ParameterCallbackRegistration(Action unregister)
        {
            _unregister = unregister;
        }

        public void Dispose()
        {
            _unregister();
            GC.SuppressFinalize(this);
        }

        public void Unregister()
        {
            _unregister();
        }
    }
}

/// <summary>
/// A parameter class that acts on a property in a class, allowing the Web UI to read and write the property value. The property must be of a type that implements IEquatable<T>, IFormattable, and IParsable<T>.
/// </summary>
public class PropertyParameter<T> : Parameter<T> where T : notnull, IEquatable<T>, IFormattable, IParsable<T>
{
    private readonly object _target;
    private readonly PropertyInfo _property;
    public PropertyParameter(string label, object target, string propertyName, string? name = null)
        : base(label, name)
    {
        _target = target;
        _property = target.GetType().GetProperty(propertyName) ?? throw new ArgumentException($"Property '{propertyName}' not found on target of type '{target.GetType().Name}'.");
    }

    public override T Value
    {
        get => (T)_property.GetValue(_target)!;
        set => _property.SetValue(_target, value);
    }
}

/// <summary>
/// Marks a public property as a configurable parameter that can be exposed in the Web UI.
/// </summary>
/// <remarks>
/// Logic authors can use this attribute to surface a setting without manually constructing <see cref="Parameter{T}"/> instances.
/// Typical usage is to decorate a property on the logic itself and then expose it via <see cref="IParametersContainer"/> and <see cref="ParameterExtensions.GetParametersFromAttributes(object)"/>.
/// <code>
/// public sealed class MyLogic : LogicBase, IParametersContainer
/// {
///     [Parameter("Threshold", description: "Minimum temperature before action is triggered")]
///     public int Threshold { get; set; } = 20;
///
///     public IReadOnlyCollection&lt;IParameter&gt; Parameters =&gt; this.GetParametersFromAttributes().ToArray();
/// }
/// </code>
/// The property must be public, writable, and string-parsable. Primitive types such as <c>bool</c>, <c>int</c>, <c>float</c>, and <c>string</c> work directly; other types must implement <c>IEquatable&lt;T&gt;</c>, <c>IFormattable</c>, and <c>IParsable&lt;T&gt;</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ParameterAttribute : Attribute
{
    public ParameterAttribute(string label, string? name = null, string? description = null)
    {
        Label = label;
        Name = name;
        Description = description;
    }

    public string Label { get; }
    public string? Name { get; }
    public string? Description { get; }
}

public static class ParameterExtensions
{
    /// <summary>
    /// Converts a value to a Parameter instance.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="label"></param>
    /// <param name="name"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static Parameter<T> ToParameter<T>(this T value, string label, string? name = null) where T : notnull, IEquatable<T>, IFormattable, IParsable<T>
    {
        var parameter = new Parameter<T>(label, name);
        parameter.Value = value;
        return parameter;
    }

    /// <summary>
    /// Converts a property of an object to a Parameter instance.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="propertyName"></param>
    /// <param name="label"></param>
    /// <param name="name"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static PropertyParameter<T> ToPropertyParameter<T>(this object target, string propertyName, string label, string? name = null) where T : notnull, IEquatable<T>, IFormattable, IParsable<T>
    {
        return new PropertyParameter<T>(label, target, propertyName, name);
    }

    /// <summary>
    /// Converts <see cref="ParameterAttribute"/>ed properties of an object to a list of <see cref="IParameter"/>s.
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    public static IEnumerable<IParameter> GetParametersFromAttributes(this object target)
    {
        var parameters = new List<IParameter>();
        var properties = target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var property in properties)
        {
            var attribute = property.GetCustomAttribute<ParameterAttribute>();
            if (attribute is null)
            {
                continue;
            }

            // if the property implements IParameter, use that instead of creating a new PropertyParameter<T> instance.
            if (typeof(IParameter).IsAssignableFrom(property.PropertyType))
            {
                var parameter = (IParameter)property.GetValue(target)!;
                parameters.Add(parameter);
            }
            else
            {
                var parameterType = typeof(PropertyParameter<>).MakeGenericType(property.PropertyType);
                var args = new object?[] { attribute.Label, target, property.Name, attribute.Name };
                var parameter = (IParameter)(Activator.CreateInstance(parameterType, args) ?? throw new InvalidOperationException($"Could not create parameter for property '{property.Name}' on target of type '{target.GetType().Name}'."));
                parameters.Add(parameter);
            }
        }
        return parameters;
    }
}