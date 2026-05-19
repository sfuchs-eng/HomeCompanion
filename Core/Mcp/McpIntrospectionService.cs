using HomeCompanion.Logics;
using HomeCompanion.Values;
using System.Reflection;

namespace HomeCompanion.Core.Mcp;

/// <summary>
/// Default read-only implementation of <see cref="IMcpIntrospectionService"/>.
/// </summary>
public sealed class McpIntrospectionService(
    IEnumerable<IValuesContainer> containers,
    IEnumerable<ILogic> logics) : IMcpIntrospectionService
{
    private readonly IValuesContainer[] _containers = [.. containers];
    private readonly ILogic[] _logics = [.. logics];

    public IReadOnlyList<McpValuesContainerInfo> ListValuesContainers()
    {
        return [.. _containers
            .Select(c => new McpValuesContainerInfo(
                c.GetType().FullName ?? c.GetType().Name,
                c.GetType().Assembly.GetName().Name ?? string.Empty,
                c.GetValues().Count()))
            .OrderBy(x => x.Type, StringComparer.Ordinal)];
    }

    public IReadOnlyList<McpContainerValuePropertyInfo> ListContainerValueProperties(string containerType)
    {
        if (string.IsNullOrWhiteSpace(containerType))
            return [];

        var container = FindContainer(containerType);
        if (container is null)
            return [];

        var containerClrType = container.GetType();
        var containerClrTypeName = containerClrType.FullName ?? containerClrType.Name;

        return [.. GetPublicValueProperties(containerClrType)
            .Select(prop =>
            {
                var value = prop.GetValue(container) as IValue;
                var valueTypeName = value?.ValueType.FullName
                    ?? ExtractIValueType(prop.PropertyType)?.FullName
                    ?? typeof(object).FullName
                    ?? nameof(Object);
                return new McpContainerValuePropertyInfo(
                    containerClrTypeName,
                    prop.Name,
                    valueTypeName,
                    value?.Name,
                    value?.Label);
            })
            .OrderBy(x => x.PropertyName, StringComparer.Ordinal)];
    }

    public McpValueInfo? GetValueInfo(string containerType, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(containerType) || string.IsNullOrWhiteSpace(propertyName))
            return null;

        var container = FindContainer(containerType);
        if (container is null)
            return null;

        var property = GetPublicValueProperties(container.GetType())
            .FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.Ordinal));
        if (property is null)
            return null;

        var value = property.GetValue(container) as IValue;
        if (value is null)
            return null;

        var mappings = value.BusMappings
            .Select(entry => new McpValueBusMappingInfo(
                entry.Key.ToString() ?? string.Empty,
                entry.Value.BusId,
                entry.Value.Address,
                entry.Value.Communication.ToString(),
                entry.Value.Config?.FormatConfiguration()))
            .OrderBy(x => x.BusIdentifier, StringComparer.Ordinal)
            .ToArray();

        return new McpValueInfo(
            container.GetType().FullName ?? container.GetType().Name,
            property.Name,
            value.Name,
            value.Label,
            value.ValueType.FullName ?? value.ValueType.Name,
            value.Status.ToString(),
            CreateSerializableValueSnapshot(value.OValue),
            mappings);
    }

    public IReadOnlyList<McpLogicInfo> ListLogicInstances()
    {
        return [.. _logics
            .Select(l => new McpLogicInfo(
                l.GetType().FullName ?? l.GetType().Name,
                l.GetType().Assembly.GetName().Name ?? string.Empty,
                l.IsEnabled))
            .OrderBy(x => x.Type, StringComparer.Ordinal)];
    }

    private IValuesContainer? FindContainer(string containerType)
    {
        return _containers.FirstOrDefault(container =>
            string.Equals(container.GetType().FullName, containerType, StringComparison.Ordinal)
            || string.Equals(container.GetType().Name, containerType, StringComparison.Ordinal));
    }

    private static object? CreateSerializableValueSnapshot(object? value)
    {
        if (value is null)
            return null;

        var valueType = value.GetType();
        if (valueType.IsPrimitive
            || value is string
            || value is decimal
            || value is Guid
            || value is DateTime
            || value is DateTimeOffset
            || value is TimeSpan)
        {
            return value;
        }

        // Keep MCP output stable for complex objects without relying on custom converters.
        return value.ToString();
    }

    private static IEnumerable<PropertyInfo> GetPublicValueProperties(Type containerType)
    {
        return containerType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(prop => prop.GetMethod is not null && typeof(IValue).IsAssignableFrom(prop.PropertyType));
    }

    private static Type? ExtractIValueType(Type propertyType)
    {
        if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(IValue<>))
            return propertyType.GetGenericArguments()[0];

        var typedIValue = propertyType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValue<>));

        return typedIValue?.GetGenericArguments()[0];
    }
}
