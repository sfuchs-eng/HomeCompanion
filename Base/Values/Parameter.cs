using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace HomeCompanion.Values;

// for IValue, implement IParameter straight into IValue and BaseValue, and implement IParameter in ValueBase<T> for the generic version. This allows logics to expose parameters that can be configured in the Web UI, and also allows mapping IValues to IParameters for writing to IValues from the Web UI.
// for basic types (bool, int, float, string), implement IParameter in a Parameter<T> class here that can be used to expose parameters of those types.

public class Parameter<T> : IParameter where T : notnull, IParsable<T>
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

    public virtual string FormatValue()
    {
        return Value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : Value.ToString() ?? string.Empty;
    }

    public virtual bool SetValueFromString(string value, out string? errorMessage)
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

    protected void OnChanged()
    {
        foreach (var callback in _changedCallbacks)
        {
            callback(this);
        }
    }

    protected List<Action<IParameter>> _changedCallbacks = new List<Action<IParameter>>();

    public virtual IParameterCallbackRegistration RegisterChangedCallback(Action<IParameter> callback)
    {
        _changedCallbacks.Add(callback);
        return new ParameterCallbackRegistration(() => _changedCallbacks.Remove(callback));
    }

    protected sealed class ParameterCallbackRegistration : IParameterCallbackRegistration
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
/// IValue to IParameter adapter for IValues that are not IParameters. This allows exposing IValues as IParameters in the Web UI.
/// </summary>
public class ValueParameterAdapter<T> : Parameter<T> where T : notnull, IParsable<T>
{
    private readonly IValue<T> _value;

    public ValueParameterAdapter(IValue<T> value)
        : base(value.Label ?? value.Name ?? throw new ArgumentException("Value must have a label or a name."), value.Name)
    {
        _value = value;
    }

    private void OnValueChanged(object? sender, EventArgs e)
    {
        base.Value = _value.Value;
    }

    public override T Value
    {
        get => _value.Value;
        set
        {
            // should launch a Changed event if the value is different, but the IValue<T> implementation should handle that.
            _value.Write(value);
        } 
    }

    override public string FormatValue()
    {
        // if T is string, just return the value, otherwise use the IValue<T> Format method to format the value for display.
        if (typeof(T) == typeof(string))
        {
            return _value.Value?.ToString() ?? string.Empty;
        }
        return _value.Format(CultureInfo.InvariantCulture) ?? $"Failed to format value '{_value.Name}' of type {typeof(T).Name}";
    }

    /// <summary>
    /// Uses the underlying IValue<T> to parse the string value and write it to the IValue<T>. Returns true if successful, false otherwise. If parsing fails, an error message is returned.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="errorMessage"></param>
    /// <returns></returns>
    public override bool SetValueFromString(string value, out string? errorMessage)
    {
        if (_value.TryParseValue(value, out var parsedValue, out errorMessage))
        {
            if (parsedValue is T typedValue)
            {
                _value.Write(typedValue);
                return true;
            }
            else
            {
                errorMessage = $"Parsed value is not of type {typeof(T).Name}.";
                return false;
            }
        }
        else
        {
            return false;
        }
    }

    public override IParameterCallbackRegistration RegisterChangedCallback(Action<IParameter> callback)
    {
        // we need to register the callback to the underlying IValue<T> Changed event, and also to our own Changed event so that we can notify the callback when the value changes.
        // when the last callback is unregistered, we should unregister from the underlying IValue<T> Changed event to avoid memory leaks.
        if (_changedCallbacks.Count == 0)
        {
            _value.Changed += OnValueChanged;
        }
        _changedCallbacks.Add(callback);
        return new ParameterCallbackRegistration(() =>
        {
            _changedCallbacks.Remove(callback);
            if (_changedCallbacks.Count == 0)
            {
                _value.Changed -= OnValueChanged;
            }
        });
    }
}

/// <summary>
/// A parameter class that acts on a property in a class, allowing the Web UI to read and write the property value. The property must be of a type that implements IParsable&lt;T&gt;.
/// </summary>
public class PropertyParameter<T> : Parameter<T> where T : notnull, IParsable<T>
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
/// Shortfall using this approach: property value changes originating from code are not automatically reflected in the Web UI unless the Web UI is refreshed.
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
/// The property must be public, writable, and string-parsable. Primitive types such as <c>bool</c>, <c>int</c>, <c>float</c>, and <c>string</c> work directly; other types must implement <c>IParsable&lt;T&gt;</c>.
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
    public static Parameter<T> ToParameter<T>(this T value, string label, string? name = null) where T : notnull, IParsable<T>
    {
        var parameter = new Parameter<T>(label, name);
        parameter.Value = value;
        return parameter;
    }

    /// <summary>
    /// IValue to IParameter adapter. Converts an IValue&lt;T&gt; to a Parameter&lt;T&gt; instance, allowing the Web UI to read and write the value. The IValue&lt;T&gt; must be of a type that implements IParsable&lt;T&gt;.
    /// </summary>
    public static IParameter? ToParameter(this IValue value)
    {
        try
        {
            var valueType = value.ValueType;
            var parameterType = typeof(ValueParameterAdapter<>).MakeGenericType(valueType);
            var parameter = (IParameter?)Activator.CreateInstance(parameterType, value);
            return parameter;
        }
        catch
        {
            return null;
        }
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
    public static PropertyParameter<T> ToPropertyParameter<T>(this object target, string propertyName, string label, string? name = null) where T : notnull, IParsable<T>
    {
        return new PropertyParameter<T>(label, target, propertyName, name);
    }

    private static bool IsSupportedParameterType(Type type)
    {
        return type.GetInterfaces().Any(i =>
            i.IsGenericType
            && i.GetGenericTypeDefinition() == typeof(IParsable<>)
            && i.GenericTypeArguments[0] == type);
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
                if (property.SetMethod is null || !property.SetMethod.IsPublic)
                {
                    throw new InvalidOperationException($"Property '{property.Name}' on target of type '{target.GetType().Name}' cannot be exposed as a parameter because it does not have a public setter.");
                }

                if (!IsSupportedParameterType(property.PropertyType))
                {
                    throw new InvalidOperationException($"Property '{property.Name}' on target of type '{target.GetType().Name}' cannot be exposed as a parameter because type '{property.PropertyType}' does not implement IParsable<{property.PropertyType.Name}>.");
                }

                var parameterType = typeof(PropertyParameter<>).MakeGenericType(property.PropertyType);
                var args = new object?[] { attribute.Label, target, property.Name, attribute.Name };
                var parameter = (IParameter)(Activator.CreateInstance(parameterType, args) ?? throw new InvalidOperationException($"Could not create parameter for property '{property.Name}' on target of type '{target.GetType().Name}'."));
                parameters.Add(parameter);
            }
        }
        return parameters;
    }
}