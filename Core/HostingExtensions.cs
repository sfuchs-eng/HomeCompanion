using HomeCompanion.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SRF.Network.Knx;

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

        // Libraries with extension methods
        builder.AddKnxIpRouting("default");

        // Custom discovery-based registrations
        builder.Services.AddConnectivityProviders();
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
        var systemPath = Path.Combine("/etc", $"{AppName}.json");
        var userPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            $"{AppName}.json");

        var sources = builder.Configuration.Sources;

        // Find the index of the first EnvironmentVariables source so we can insert before it.
        // If none is found (e.g. test host), append at the end.
        int insertAt = sources.Count;
        for (int i = 0; i < sources.Count; i++)
        {
            if (sources[i] is EnvironmentVariablesConfigurationSource)
            {
                insertAt = i;
                break;
            }
        }

        // Insert system config first, user config second — user overrides system.
        sources.Insert(insertAt, BuildJsonSource(systemPath));
        sources.Insert(insertAt + 1, BuildJsonSource(userPath));

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
    /// Registers the <see cref="EventBus"/> singleton as <see cref="IEventPublisher"/>,
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
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => t.IsClass && !t.IsAbstract && providerInterface.IsAssignableFrom(t));

        foreach (var type in providerTypes)
        {
            services.AddSingleton(type);
            services.AddSingleton(providerInterface, sp => sp.GetRequiredService(type));
            services.AddHostedService(sp => (IHostedService)sp.GetRequiredService(type));
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
            .SelectMany(a => a.GetExportedTypes())
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
}
