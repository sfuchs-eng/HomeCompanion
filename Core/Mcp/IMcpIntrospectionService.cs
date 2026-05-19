using HomeCompanion.Logics;
using HomeCompanion.Values;

namespace HomeCompanion.Core.Mcp;

/// <summary>
/// Provides read-only introspection over discovered containers, values, and logic instances for MCP tools.
/// </summary>
public interface IMcpIntrospectionService
{
    /// <summary>
    /// Lists all registered value containers.
    /// </summary>
    IReadOnlyList<McpValuesContainerInfo> ListValuesContainers();

    /// <summary>
    /// Lists all public <see cref="IValue"/> properties declared by a specific container type.
    /// </summary>
    /// <param name="containerType">Container CLR type full name.</param>
    IReadOnlyList<McpContainerValuePropertyInfo> ListContainerValueProperties(string containerType);

    /// <summary>
    /// Retrieves detailed information about a value property declared by a container.
    /// </summary>
    /// <param name="containerType">Container CLR type full name.</param>
    /// <param name="propertyName">Public property name implementing <see cref="IValue"/>.</param>
    McpValueInfo? GetValueInfo(string containerType, string propertyName);

    /// <summary>
    /// Lists all discovered logic instances.
    /// </summary>
    IReadOnlyList<McpLogicInfo> ListLogicInstances();
}

/// <summary>
/// Represents a discovered values container.
/// </summary>
/// <param name="Type">CLR type full name.</param>
/// <param name="Assembly">Assembly name containing the type.</param>
/// <param name="ValueCount">Number of values returned by <see cref="IValuesContainer.GetValues"/>.</param>
public sealed record McpValuesContainerInfo(string Type, string Assembly, int ValueCount);

/// <summary>
/// Represents a public value property of a container.
/// </summary>
/// <param name="ContainerType">Container CLR type full name.</param>
/// <param name="PropertyName">Property name.</param>
/// <param name="ValueType">CLR value type full name.</param>
/// <param name="Name">Optional value name.</param>
/// <param name="Label">Optional value label.</param>
public sealed record McpContainerValuePropertyInfo(
    string ContainerType,
    string PropertyName,
    string ValueType,
    string? Name,
    string? Label);

/// <summary>
/// Represents bus endpoint mapping information for a value.
/// </summary>
/// <param name="BusIdentifier">Bus identifier key used in <see cref="IValue.BusMappings"/>.</param>
/// <param name="BusId">Logical bus ID of the mapping.</param>
/// <param name="Address">Bus address/datapoint identifier.</param>
/// <param name="Communication">Communication flags configured for the mapping.</param>
/// <param name="Config">Best-effort formatted mapping config.</param>
public sealed record McpValueBusMappingInfo(
    string BusIdentifier,
    string BusId,
    string Address,
    string Communication,
    string? Config);

/// <summary>
/// Detailed information about a value instance.
/// </summary>
/// <param name="ContainerType">Container CLR type full name.</param>
/// <param name="PropertyName">Declaring public property name.</param>
/// <param name="Name">Optional value name.</param>
/// <param name="Label">Optional value label.</param>
/// <param name="ValueType">CLR value type full name.</param>
/// <param name="Status">Current value status flags.</param>
/// <param name="CurrentValue">Best-effort snapshot of current object value.</param>
/// <param name="BusMappings">Configured bus endpoint mappings.</param>
public sealed record McpValueInfo(
    string ContainerType,
    string PropertyName,
    string? Name,
    string? Label,
    string ValueType,
    string Status,
    object? CurrentValue,
    IReadOnlyList<McpValueBusMappingInfo> BusMappings);

/// <summary>
/// Represents a discovered logic instance.
/// </summary>
/// <param name="Type">CLR type full name.</param>
/// <param name="Assembly">Assembly name containing the logic type.</param>
/// <param name="IsEnabled">Current enabled flag.</param>
public sealed record McpLogicInfo(string Type, string Assembly, bool IsEnabled);
