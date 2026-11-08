using System.Reflection;
using HomeCompanion.Extensions;
using Microsoft.Extensions.Options;

namespace HomeCompanion.Server;

public sealed class HomeCompanionPageRegistry(IOptions<HomeCompanionPageRegistrationOptions> options) : IHomeCompanionPageRegistry
{
    private readonly List<HomeCompanionPageDescriptor> _pages = [.. options.Value.Pages];
    private readonly HashSet<Assembly> _assemblies = [.. options.Value.Assemblies, .. DiscoverHomeCompanionAssemblies()];

    public IReadOnlyList<HomeCompanionPageDescriptor> Pages =>
        _pages
            .OrderBy(page => page.Order)
            .ThenBy(page => page.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<Assembly> AdditionalAssemblies => _assemblies.ToList();

    public void RegisterPage(HomeCompanionPageDescriptor page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (_pages.Any(existing => string.Equals(existing.Route, page.Route, StringComparison.OrdinalIgnoreCase)))
            return;

        _pages.Add(page);
    }

    public void RegisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assemblies.Add(assembly);
    }

    private static IEnumerable<Assembly> DiscoverHomeCompanionAssemblies()
    {
        var discoveredAssemblies = new HashSet<Assembly>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var assemblyName = assembly.GetName().Name;
            if (assemblyName is null || !assemblyName.StartsWith("HomeCompanion", StringComparison.OrdinalIgnoreCase))
                continue;

            if (assembly.GetTypes().Any(type => type.IsDefined(typeof(Microsoft.AspNetCore.Components.RouteAttribute), inherit: false)))
                discoveredAssemblies.Add(assembly);
        }

        if (string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
            return discoveredAssemblies;

        foreach (var dllPath in Directory.EnumerateFiles(AppContext.BaseDirectory, "HomeCompanion*.dll", SearchOption.TopDirectoryOnly))
        {
            Assembly? assembly = null;
            try
            {
                assembly = Assembly.LoadFrom(dllPath);
            }
            catch (FileLoadException)
            {
                // assembly is already loaded or otherwise not ready to be reloaded; use the loaded copy instead.
            }
            catch (BadImageFormatException)
            {
                // ignore non-managed assemblies in the app base directory.
            }
            catch (FileNotFoundException)
            {
                // ignore assemblies that disappear during discovery.
            }

            if (assembly is null)
                continue;

            var loadedAssemblyName = assembly.GetName().Name;
            if (loadedAssemblyName is null || !loadedAssemblyName.StartsWith("HomeCompanion", StringComparison.OrdinalIgnoreCase))
                continue;

            if (assembly.GetTypes().Any(type => type.IsDefined(typeof(Microsoft.AspNetCore.Components.RouteAttribute), inherit: false)))
                discoveredAssemblies.Add(assembly);
        }

        return discoveredAssemblies;
    }
}
