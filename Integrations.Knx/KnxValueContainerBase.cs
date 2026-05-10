using System.Reflection;
using HomeCompanion.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SRF.Knx.Config;

namespace HomeCompanion.Integrations.Knx;

public abstract class KnxValueContainerBase : ValueContainerBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KnxValueContainerBase"/> class.
    /// This constructor performs a sanity check to ensure that all IValue<T> properties with a <see cref="IValue.BusMappings"/> entry of type <see cref="KnxBusEndpointMapping"/> have consistent types between <T> and their DPT/PDT.
    /// This helps catch code generation and configuration errors early.
    /// </summary>
    /// <param name="serviceProvider"></param>
    /// <param name="logger"></param>
    /// <returns></returns>
    public KnxValueContainerBase(IServiceProvider serviceProvider, ILogger<KnxValueContainerBase> logger) : base(logger)
    {
        var gaMeta = serviceProvider.GetService<IKnxSystemConfiguration>() ?? throw new InvalidOperationException("IKnxSystemConfiguration is required for KnxValueContainerBase but not registered in the service provider.");
        if (!GetType().IsIValuePropertiesWithCorrectValueType(gaMeta, logger))
        {
            throw new InvalidOperationException("One or more IValue properties have incorrect value types.");
        }
    }
}

/// <summary>
/// Helper methods for working with KNX value containers.
/// As there might be arbitrary types implementing <see cref="IValuesContainer"/> with KNX mapped <see cref="IValue"/>,
/// we provide the KNX oriented helper methods as extension methods to <see cref="Type"/>.
/// </summary>
public static class KnxValueContainerHelpers
{
    /// <summary>
    /// Gets all public instance properties of the given type that implement IValue<T> for any T.
    /// </summary>
    /// <remarks>
    /// Provided by <see cref="KnxValueContainerHelpers"/> but works for any Type with IValue properties.
    /// </remarks>
    /// <param name="type"></param>
    /// <returns>Properties implementing IValue</returns>
    public static PropertyInfo[] GetIValueProperties(this Type type)
    {
        return [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => typeof(IValue).IsAssignableFrom(p.PropertyType))];
    }

    public static PropertyInfo[] GetIValueProperties<T>(this Type type)
    {
        return [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => typeof(IValue<T>).IsAssignableFrom(p.PropertyType))];
    }

    public static bool IsIValuePropertiesWithCorrectValueType(this Type type, IKnxSystemConfiguration knxConfig, ILogger logger)
    {
        logger.LogTrace("Checking IValue properties of type {TypeName} for correct value types based on KNX configuration.", type.Name);
        var properties = type.GetIValueProperties();
        var allValid = true;

        foreach (var property in properties)
        {
            var valueInterface = property.PropertyType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValue<>));

            if (valueInterface == null) continue;

            if (valueInterface.GetProperty("BusMappings")?.GetValue(null) is not Dictionary<object, IValueBusEndpointMapping> busMappings)
                continue;

            foreach (var busMapping in busMappings.Select(kv => kv.Value).Select(m => m as KnxBusEndpointMapping).Where(m => m != null))
            {
                var valueType = valueInterface.GetGenericArguments()[0];
                var expectedValueType = knxConfig.GetGroupAddressMeta(busMapping!.GroupAddress).Dpt.ValueType;
                var gaName = knxConfig.GetGroupAddressMetaOrNull(busMapping.GroupAddress)?.Name ?? busMapping.GroupAddress.ToString();

                if (valueType != expectedValueType)
                {
                    logger.LogWarning("Type mismatch for property '{PropertyName}': IValue<{ValueType}> has a KNX bus mapping with DPT {DataPointType} and PDT type {ExpectedValueType}.", property.Name, valueType.Name, gaName, expectedValueType.Name);
                    allValid = false;
                }
            }
        }

        logger.LogTrace("Completed checking IValue properties of type {TypeName}. All valid: {AllValid}", type.Name, allValid);
        return allValid;
    }
}
