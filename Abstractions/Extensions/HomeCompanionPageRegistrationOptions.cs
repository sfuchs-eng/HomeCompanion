using System.Reflection;

namespace HomeCompanion.Extensions;

public sealed class HomeCompanionPageDescriptor
{
    public required string Title { get; init; }
    public required string Route { get; init; }
    public string Icon { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int Order { get; init; } = 100;
}

public sealed class HomeCompanionPageRegistrationOptions
{
    public List<HomeCompanionPageDescriptor> Pages { get; } = [];
    public List<Assembly> Assemblies { get; } = [];
}

public interface IHomeCompanionPageRegistry
{
    IReadOnlyList<HomeCompanionPageDescriptor> Pages { get; }
    IReadOnlyList<Assembly> AdditionalAssemblies { get; }
    void RegisterPage(HomeCompanionPageDescriptor page);
    void RegisterAssembly(Assembly assembly);
}
