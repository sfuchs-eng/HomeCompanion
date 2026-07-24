using HomeCompanion.Calendar;
using HomeCompanion.Events;
using System.Reflection;

namespace HomeCompanion.Core.Calendar;

internal sealed class AttributedCalendarEventTypeRegistry : ICalendarEventTypeRegistry
{
    private readonly Lazy<IReadOnlyList<CalendarEventTypeDescriptor>> _descriptors = new(DiscoverDescriptors);
    private readonly Lazy<Dictionary<string, Type>> _typesByName = new(() =>
        DiscoverTypes().ToDictionary(k => k.AssemblyQualifiedName!, v => v, StringComparer.Ordinal));

    public IReadOnlyList<CalendarEventTypeDescriptor> ListEventTypes() => _descriptors.Value;

    public Type? ResolveEventType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;
        return _typesByName.Value.TryGetValue(typeName, out var resolved) ? resolved : null;
    }

    private static IReadOnlyList<CalendarEventTypeDescriptor> DiscoverDescriptors()
    {
        return DiscoverTypes()
            .Select(type =>
            {
                var attribute = type.GetCustomAttributes(typeof(CalendarEventTypeAttribute), false)
                    .Cast<CalendarEventTypeAttribute>()
                    .Single();

                return new CalendarEventTypeDescriptor
                {
                    TypeName = type.AssemblyQualifiedName!,
                    TypeShortName = type.Name,
                    DisplayName = attribute.DisplayName,
                    Description = attribute.Description,
                    Category = attribute.Category,
                };
            })
            .OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<Type> DiscoverTypes()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(GetTypesSafe)
            .Where(t =>
                t.IsClass
                && !t.IsAbstract
                && typeof(ICalendarEvent).IsAssignableFrom(t)
                && t.GetConstructor(Type.EmptyTypes) is not null
                && t.GetCustomAttributes(typeof(CalendarEventTypeAttribute), false).Length == 1
                && t.AssemblyQualifiedName is not null)
            .ToArray();
    }

    private static IEnumerable<Type> GetTypesSafe(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
