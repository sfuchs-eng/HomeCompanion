using HomeCompanion.Base.Model;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HomeCompanion.Core.Model;

internal sealed class ModelValueBinder(
    IValueProvider valueReferenceProvider,
    ILogger<ModelValueBinder> logger)
{
    private static readonly BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly IValueProvider _valueReferenceProvider = valueReferenceProvider;
    private readonly ILogger<ModelValueBinder> _logger = logger;

    public void Bind(Base.Model.Model model)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        TraverseAndBind(model, CfgModel.ConfigurationKey, visited);
    }

    private void TraverseAndBind(object? node, string path, HashSet<object> visited)
    {
        if (node is null || IsLeafType(node.GetType()))
            return;

        if (node is IValue || node is CfgEntity)
            return;

        if (!visited.Add(node))
            return;

        if (node is IConfigBackedModelEntity configBacked)
        {
            BindConfigBackedEntity(configBacked, path);
        }

        if (node is IDictionary dict)
        {
            foreach (DictionaryEntry entry in dict)
            {
                var childPath = $"{path}:{entry.Key}";
                TraverseAndBind(entry.Value, childPath, visited);
            }

            return;
        }

        if (node is IEnumerable enumerable and not string)
        {
            var index = 0;
            foreach (var item in enumerable)
            {
                var childPath = $"{path}[{index}]";
                TraverseAndBind(item, childPath, visited);
                index++;
            }

            return;
        }

        foreach (var property in node.GetType().GetProperties(InstanceFlags))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
                continue;

            object? child;
            try
            {
                child = property.GetValue(node);
            }
            catch
            {
                continue;
            }

            if (child is null)
                continue;

            if (child is IValue || child is CfgEntity)
                continue;

            var childPath = $"{path}:{property.Name}";
            TraverseAndBind(child, childPath, visited);
        }
    }

    private void BindConfigBackedEntity(IConfigBackedModelEntity entity, string path)
    {
        var cfg = entity.Configuration;
        var entityType = entity.GetType();
        var targetProperties = entityType
            .GetProperties(InstanceFlags)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && typeof(IValue).IsAssignableFrom(p.PropertyType));

        foreach (var targetProperty in targetProperties)
        {
            var bindingAttribute = targetProperty.GetCustomAttribute<ModelValueBindingAttribute>();
            var sourcePropertyName = bindingAttribute?.SourceConfigPropertyName ?? $"{targetProperty.Name}Reference";
            var sourceProperty = cfg.GetType().GetProperty(sourcePropertyName, InstanceFlags | BindingFlags.IgnoreCase);

            if (sourceProperty is null)
            {
                if (bindingAttribute is not null)
                {
                    throw new InvalidOperationException(
                        $"Model binding at '{path}' requires source config property '{sourcePropertyName}' for target '{entityType.Name}.{targetProperty.Name}', but it was not found on cfg type '{cfg.GetType().Name}'.");
                }

                continue;
            }

            if (sourceProperty.PropertyType != typeof(string))
                throw new InvalidOperationException(
                    $"Model binding at '{path}' requires source config property '{cfg.GetType().Name}.{sourceProperty.Name}' to be of type string.");

            var reference = (string?)sourceProperty.GetValue(cfg);
            if (string.IsNullOrWhiteSpace(reference))
                continue;

            var value = _valueReferenceProvider.Resolve(reference);

            if (!targetProperty.PropertyType.IsAssignableFrom(value.GetType()))
            {
                throw new InvalidOperationException(
                    $"Model binding at '{path}' cannot assign resolved value type '{value.GetType().Name}' to target '{entityType.Name}.{targetProperty.Name}' of type '{targetProperty.PropertyType.Name}'.");
            }

            if (bindingAttribute?.RequireNumeric == true)
                EnsureNumericValue(value, path, sourceProperty.Name);

            if (bindingAttribute?.RequiredValueType is not null)
            {
                var requiredType = Nullable.GetUnderlyingType(bindingAttribute.RequiredValueType) ?? bindingAttribute.RequiredValueType;
                var actualType = Nullable.GetUnderlyingType(value.ValueType) ?? value.ValueType;
                if (actualType != requiredType)
                {
                    throw new InvalidOperationException(
                        $"Model binding at '{path}' requires '{entityType.Name}.{targetProperty.Name}' to resolve value type '{requiredType.Name}', but got '{actualType.Name}'.");
                }
            }

            targetProperty.SetValue(entity, value);

            _logger.LogInformation(
                "Bound model value at {ModelPath}. Target={Target}, SourceProperty={SourceProperty}, Reference={Reference}.",
                path,
                $"{entityType.Name}.{targetProperty.Name}",
                sourceProperty.Name,
                reference);
        }
    }

    private static bool IsLeafType(Type type)
    {
        var candidate = Nullable.GetUnderlyingType(type) ?? type;
        return candidate.IsPrimitive
               || candidate.IsEnum
               || candidate == typeof(string)
               || candidate == typeof(decimal)
               || candidate == typeof(DateTime)
               || candidate == typeof(DateTimeOffset)
               || candidate == typeof(TimeSpan)
               || candidate == typeof(Guid);
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();

        bool IEqualityComparer<object>.Equals(object? x, object? y) => ReferenceEquals(x, y);

        int IEqualityComparer<object>.GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private static void EnsureNumericValue(IValue value, string path, string propertyName)
    {
        var type = Nullable.GetUnderlyingType(value.ValueType) ?? value.ValueType;
        if (!IsNumericType(type))
        {
            throw new InvalidOperationException(
                $"Model binding at '{path}' requires a numeric value for '{propertyName}', but resolved '{value.Name ?? "<unnamed>"}' of type '{value.ValueType.Name}'.");
        }
    }

    private static bool IsNumericType(Type type)
        => type == typeof(byte)
           || type == typeof(sbyte)
           || type == typeof(short)
           || type == typeof(ushort)
           || type == typeof(int)
           || type == typeof(uint)
           || type == typeof(long)
           || type == typeof(ulong)
           || type == typeof(float)
           || type == typeof(double)
           || type == typeof(decimal);
}
