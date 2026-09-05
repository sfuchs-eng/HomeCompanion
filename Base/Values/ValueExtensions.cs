using System.Reflection;

namespace HomeCompanion.Values;

public static class ValueExtensions
{
    /// <summary>
    /// Gets all public instance properties of the given type that implement IValue.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static PropertyInfo[] GetIValueProperties(this Type type)
    {
        return [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => typeof(IValue).IsAssignableFrom(p.PropertyType))];
    }

    /// <summary>
    /// Gets all public instance properties of the given type that implement IValue<T> for any T.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="type"></param>
    /// <returns>Properties implementing IValue</returns>
    public static PropertyInfo[] GetIValueProperties<T>(this Type type)
    {
        return [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => typeof(IValue<T>).IsAssignableFrom(p.PropertyType))];
    }

}
