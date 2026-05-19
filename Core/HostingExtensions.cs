using HomeCompanion.Values;
using HomeCompanion.Extensions;
using HomeCompanion.Logics;
using HomeCompanion.Persistence;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SRF.Network.OpenHab;
using HomeCompanion.Events;
using HomeCompanion.Core.Events;
using HomeCompanion.Core.Logics;
using HomeCompanion.Core.Mcp;
using HomeCompanion.Abstractions;
using HomeCompanion.Core.Persistence;

namespace HomeCompanion.Core;

public static class HostingExtensions
{
    private const string AppName = "HomeCompanion";

    /// <summary>
    /// Adds HomeCompanion core services to the dependency injection container.
    /// </summary>
    /// <param name="builder">The IHostApplicationBuilder to add services to.</param>
    /// <returns>The modified IHostApplicationBuilder for chaining.</returns>
    public static IHostApplicationBuilder AddHomeCompanionCore(this IHostApplicationBuilder builder)
    {
        // Configure
        builder.AddHomeCompanionConfiguration();
        builder.Services.Configure<CoreOptions>(builder.Configuration.GetSection(AppName));

        // Core services
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddEventBus();
        builder.Services.TryAddSingleton<ValuesManager>();
        builder.Services.TryAddSingleton<IValuesManager>(sp => sp.GetRequiredService<ValuesManager>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ValuesManager>());
        builder.Services.TryAddSingleton<IStateStore, JsonFilesStateStore>();
        builder.Services.TryAddSingleton<IStateInitializationManager, StateInitializationManager>();
        builder.Services.AddHostedService<StateInitializationManagerHostedService>();
        builder.Services.TryAddSingleton<IMcpIntrospectionService, McpIntrospectionService>();
        builder.Services.AddOpenHabConnector();
        builder.Services.TryAddSingleton<HomeCompanionLifeCycleSynchronization>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<HomeCompanionLifeCycleSynchronization>());
        builder.Services.TryAddSingleton<IHomeCompanionLifeCycleSynchronization>(sp => sp.GetRequiredService<HomeCompanionLifeCycleSynchronization>());

        // Load assemblies from the application base directory and optional extensions directory before scanning.
        // Reference-walk via AppDomain is unreliable (assemblies load lazily; entry assembly is null under dotnet watch).
        var extensionsPath = builder.Configuration.GetSection(AppName)[nameof(CoreOptions.ExtensionsPath)];
        LoadAssembliesFromDirectory(AppContext.BaseDirectory);
        if (!string.IsNullOrWhiteSpace(extensionsPath))
            LoadAssembliesFromDirectory(extensionsPath);

        // Custom discovery-based registrations
        builder.Services.AddConnectivityProviders();
        builder.Services.AddValuesContainers();
        builder.AddExtensions();
        builder.Services.AddLogics();
        builder.Services.AddLogicManager();

        return builder;
    }

    /// <summary>
    /// Inserts system-wide and per-user JSON configuration files into the configuration pipeline,
    /// after the standard <c>appsettings.json</c> / <c>appsettings.{env}.json</c> sources and
    /// before environment variables, so environment variables can still override them.
    /// </summary>
    /// <remarks>
    /// Files loaded, in order (later sources override earlier ones):
    /// <list type="number">
    ///   <item><c>/etc/HomeCompanion.json</c> — system-wide defaults</item>
    ///   <item><c>$XDG_CONFIG_HOME/HomeCompanion.json</c> (<c>~/.config/HomeCompanion.json</c> on Linux) — per-user overrides</item>
    /// </list>
    /// Both files are optional; missing files are silently skipped.
    /// Changes to either file are picked up at runtime without a restart (<c>reloadOnChange: true</c>).
    /// </remarks>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/> to configure.</param>
    /// <returns>The modified <see cref="IHostApplicationBuilder"/> for chaining.</returns>
    public static IHostApplicationBuilder AddHomeCompanionConfiguration(this IHostApplicationBuilder builder)
    {
        string[] extraPaths =
        [
            // later ones override earlier, so system-wide defaults come first and user overrides come after
            Path.Combine("/etc", $"{AppName}.json"),
            Path.Combine("/etc/homecompanion", $"{AppName}.json"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
                $"{AppName}.json")
        ];

        var sources = builder.Configuration.Sources;

        // Keep env vars and command line as highest-precedence sources by temporarily
        // removing and re-adding them after the custom JSON files.
        var tailSources = sources
            .Where(s => s is EnvironmentVariablesConfigurationSource || s.GetType().Name == "CommandLineConfigurationSource")
            .ToArray();

        foreach (var source in tailSources)
            sources.Remove(source);

        // Remove previous HomeCompanion JSON sources to keep this operation idempotent.
        for (int i = sources.Count - 1; i >= 0; i--)
        {
            if (sources[i] is JsonConfigurationSource jsonSource)
            {
                var path = jsonSource.Path;
                if (extraPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    sources.RemoveAt(i);
                }
            }
        }

        // Append in order of entries
        foreach (var path in extraPaths)
        {
            sources.Add(BuildJsonSource(path));
        }

        foreach (var source in tailSources)
            sources.Add(source);

        return builder;
    }

    private static JsonConfigurationSource BuildJsonSource(string path)
    {
        var source = new JsonConfigurationSource
        {
            Path = path,
            Optional = true,
            ReloadOnChange = true,
        };
        source.ResolveFileProvider();
        return source;
    }

    /// <summary>
    /// Registers services for all extensions implementing <see cref="IExtensionRegistration"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/> to configure.</param>
    /// <returns>The modified <see cref="IHostApplicationBuilder"/> for chaining.</returns>
    public static IHostApplicationBuilder AddExtensions(this IHostApplicationBuilder builder)
    {
        var extensionInterface = typeof(IExtensionRegistration);
        var extensionTypes = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(GetExportedTypesSafe)
            .Where(t => t.IsClass && !t.IsAbstract && extensionInterface.IsAssignableFrom(t));

        using var tempProvider = builder.Services.BuildServiceProvider();
        var context = new ExtensionRegistrationContext(builder);

        foreach (var extensionType in extensionTypes)
        {
            IExtensionRegistration extension;
            try
            {
                extension = (IExtensionRegistration)ActivatorUtilities.CreateInstance(tempProvider, extensionType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to instantiate extension '{extensionType.FullName}'.",
                    ex);
            }

            extension.RegisterServices(context);
        }
        return builder;
    }

    /// <summary>
    /// Registers <see cref="EventBus"/> singleton as <see cref="IEventPublisher"/>,
    /// <see cref="IEventSubscriber"/>, and <see cref="IHostedService"/>.
    /// </summary>
    public static IServiceCollection AddEventBus(this IServiceCollection services)
    {
        services.AddSingleton<EventBus>();
        services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<EventBus>());
        services.AddSingleton<IEventSubscriber>(sp => sp.GetRequiredService<EventBus>());
        services.AddHostedService(sp => sp.GetRequiredService<EventBus>());

        return services;
    }

    /// <summary>
    /// Scans all assemblies loaded in the current <see cref="AppDomain"/> for concrete types that implement
    /// <see cref="IConnectivityProvider"/> and registers each as a singleton <see cref="IConnectivityProvider"/>
    /// and as an <see cref="IHostedService"/>.
    /// </summary>
    /// <remarks>
    /// Discovery is performed once at registration time against <see cref="AppDomain.CurrentDomain"/>.
    /// Any assembly loaded after this call will not be discovered automatically.
    /// Each provider is registered as a singleton; the <see cref="IHostedService"/> registration forwards to the
    /// same singleton instance so the host manages the connection lifecycle.
    /// </remarks>
    public static IServiceCollection AddConnectivityProviders(this IServiceCollection services)
    {
        var providerInterface = typeof(IConnectivityProvider);

        var providerTypes = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(GetExportedTypesSafe)
            .Where(t => t.IsClass && !t.IsAbstract && providerInterface.IsAssignableFrom(t));

        foreach (var type in providerTypes)
        {
            services.AddSingleton(type);
            services.AddSingleton(providerInterface, sp => sp.GetRequiredService(type));
            services.AddSingleton(typeof(IHostedService), sp => (IHostedService)sp.GetRequiredService(type));
        }

        return services;
    }

    /// <summary>
    /// Scans all assemblies loaded in the current <see cref="AppDomain"/> for concrete types that implement
    /// <see cref="IValuesContainer"/> and registers each as a singleton of its own type and as
    /// <see cref="IValuesContainer"/>.
    /// </summary>
    /// <remarks>
    /// Discovery is performed once at registration time against <see cref="AppDomain.CurrentDomain"/>.
    /// Any assembly loaded after this call will not be discovered automatically.
    /// </remarks>
    public static IServiceCollection AddValuesContainers(this IServiceCollection services)
    {
        var containerInterface = typeof(IValuesContainer);

        var containerTypes = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(GetExportedTypesSafe)
            .Where(t => t.IsClass && !t.IsAbstract && containerInterface.IsAssignableFrom(t));

        Console.Error.WriteLine($"Discovered {containerTypes.Count()} values containers:");

        foreach (var type in containerTypes)
        {
            Console.Error.WriteLine($"- Registering values container: {type.FullName}");
            services.TryAddSingleton(type);
            services.AddSingleton(containerInterface, sp => sp.GetRequiredService(type));
        }

        return services;
    }

    /// <summary>
    /// Scans all assemblies loaded in the current <see cref="AppDomain"/> for concrete types that implement
    /// <see cref="ILogic"/> and registers each as a singleton <see cref="ILogic"/>.
    /// </summary>
    /// <remarks>
    /// Discovery is performed once at registration time against <see cref="AppDomain.CurrentDomain"/>.
    /// Any assembly loaded after this call will not be discovered automatically.
    /// </remarks>
    public static IServiceCollection AddLogics(this IServiceCollection services)
    {
        var logicInterface = typeof(ILogic);

        var logicTypes = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(GetExportedTypesSafe)
            .Where(t => t.IsClass && !t.IsAbstract && logicInterface.IsAssignableFrom(t));

        foreach (var type in logicTypes)
        {
            services.TryAddSingleton(type);
            services.AddSingleton(logicInterface, sp => (ILogic)sp.GetRequiredService(type));
        }

        return services;
    }

    /// <summary>
    /// Registers <see cref="LogicManager"/> as a hosted service responsible for initializing all
    /// <see cref="ILogic"/> instances in dependency order after connectivity providers are ready.
    /// </summary>
    public static IServiceCollection AddLogicManager(this IServiceCollection services)
    {
        services.AddHostedService<LogicManager>();
        return services;
    }

    /// <summary>
    /// Returns the exported types of an assembly, or an empty array if the assembly fails to enumerate its types
    /// (e.g. due to a <see cref="System.Reflection.ReflectionTypeLoadException"/>).
    /// </summary>
    private static Type[] GetExportedTypesSafe(System.Reflection.Assembly assembly)
    {
        try { return assembly.GetExportedTypes(); }
        catch { return []; }
    }

    /// <summary>
    /// Loads all <c>*.dll</c> files from <paramref name="directory"/> into the current <see cref="AppDomain"/>.
    /// Files that are already loaded or that fail to load are silently skipped.
    /// </summary>
    private static void LoadAssembliesFromDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;

        var loadedPaths = new HashSet<string>(
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => Path.GetFullPath(a.Location)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var dll in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var fullPath = Path.GetFullPath(dll);
            if (loadedPaths.Contains(fullPath)) continue;
            try { System.Reflection.Assembly.LoadFrom(fullPath); }
            catch { /* skip assemblies that cannot be loaded */ }
        }
    }
}
