using System.Reflection;
using HomeCompanion.Extensions;
using Microsoft.Extensions.Options;

namespace HomeCompanion.Server;

public sealed class HomeCompanionPageRegistry(IOptions<HomeCompanionPageRegistrationOptions> options) : IHomeCompanionPageRegistry
{
    private readonly List<HomeCompanionPageDescriptor> _pages = [.. options.Value.Pages];
    private readonly HashSet<Assembly> _assemblies = [.. options.Value.Assemblies];

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
}
