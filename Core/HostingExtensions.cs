using HomeCompanion.Values;
using HomeCompanion.Extensions;
using HomeCompanion.Logics;
using HomeCompanion.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HomeCompanion.Events;
using HomeCompanion.Core.Events;
using HomeCompanion.Core.Logics;
using HomeCompanion.Core.Mcp;
using HomeCompanion.Abstractions;
using HomeCompanion.Core.Persistence;
using HomeCompanion.Base.Model;
using HomeCompanion.Core.Model;
using HomeCompanion.Diagnostics;
using HomeCompanion.Calendar;
using HomeCompanion.Core.Calendar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
        builder.Services.TryAddSingleton<IDiagnosticBrowser, Diagnostics.DiagnosticBrowser>();
        builder.Services.AddEventBus();
        builder.Services.TryAddSingleton<ValuesManager>();
        builder.Services.TryAddSingleton<IValuesManager>(sp => sp.GetRequiredService<ValuesManager>());
        builder.Services.AddSingleton<IDiagnosable>(sp => sp.GetRequiredService<ValuesManager>());
        builder.Services.TryAddSingleton<IValueProvider, ValueReferenceProvider>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ValuesManager>());
        builder.Services.TryAddSingleton<IStateStore, JsonFilesStateStore>();
        builder.Services.TryAddSingleton<StateInitializationManager>();
        builder.Services.TryAddSingleton<IStateInitializationRegistrar>(sp => sp.GetRequiredService<StateInitializationManager>());
        builder.Services.TryAddSingleton<IModelFactory, ModelFactory>();
        builder.Services.TryAddSingleton<ModelValueBinder>();
        builder.Services.TryAddSingleton<ModelProvider>();
        builder.Services.TryAddSingleton<IModelProvider>(sp => sp.GetRequiredService<ModelProvider>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ModelProvider>());
        builder.Services.TryAddSingleton<LogicValueBinder>();
        builder.Services.TryAddSingleton<IMcpIntrospectionService, McpIntrospectionService>();
        builder.Services.TryAddTransient<IParametersProvider, ParametersProvider>();

        // Life cycle synchronization
        builder.Services.TryAddSingleton<HomeCompanionLifeCycleSynchronization>();
        //builder.Services.AddHostedService(sp => sp.GetRequiredService<HomeCompanionLifeCycleSynchronization>());
        builder.Services.AddHostedService<IHostedLifecycleService>(sp => sp.GetRequiredService<HomeCompanionLifeCycleSynchronization>());
        builder.Services.TryAddSingleton<IHomeCompanionLifeCycleSynchronization>(sp => sp.GetRequiredService<HomeCompanionLifeCycleSynchronization>());
        builder.Services.AddSingleton<IDiagnosable>(sp => sp.GetRequiredService<HomeCompanionLifeCycleSynchronization>());

        // Calendar services
        builder.Services.Configure<CalendarPersistenceOptions>(builder.Configuration.GetSection(CalendarPersistenceOptions.SectionName));
        builder.Services.AddDbContext<CalendarDbContext>((sp, db) =>
        {
            var options = sp.GetRequiredService<IOptions<CalendarPersistenceOptions>>().Value;
            var dbPath = ResolvePathRelativeToAppBase(options.DbPath, "state/calendar/calendar.db");
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            db.UseSqlite($"Data Source={dbPath}");
        });
        builder.Services.TryAddScoped<ICalendarEntryStore, EfCalendarEntryStore>();
        builder.Services.TryAddSingleton<ICalendarEventTypeRegistry, AttributedCalendarEventTypeRegistry>();
        builder.Services.TryAddSingleton<CalendarQuartzScheduler>();
        builder.Services.TryAddSingleton<ICalendarScheduler>(sp => sp.GetRequiredService<CalendarQuartzScheduler>());
        builder.Services.TryAddSingleton<Base.Quartz.IQuartzSchedulerConfigurator>(sp => sp.GetRequiredService<CalendarQuartzScheduler>());
        builder.Services.AddHostedService<CalendarDatabaseInitializerHostedService>();

        // Load assemblies from the application base directory and optional extensions directory before scanning.
        // Reference-walk via AppDomain is unreliable (assemblies load lazily; entry assembly is null under dotnet watch).
        var extensionsPath = builder.Configuration.GetSection(AppName)[nameof(CoreOptions.ExtensionsPath)];
        LoadAssembliesFromDirectory(AppContext.BaseDirectory);
        if (!string.IsNullOrWhiteSpace(extensionsPath))
            LoadAssembliesFromDirectory(extensionsPath);

        // Custom discovery-based registrations
        builder.AddExtensions();
        builder.Services.AddConnectivityProviders();
        builder.Services.AddValuesContainers();
        var logicEnvironmentRules = ResolveLogicEnvironmentRules(builder.Configuration);
        builder.Services.AddLogics(builder.Environment.EnvironmentName, logicEnvironmentRules);
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
    ///   <item><c>/etc/homecompanion/HomeCompanion.json</c> — system defaults in config folder style</item>
    ///   <item><c>$XDG_CONFIG_HOME/HomeCompanion.json</c> (<c>~/.config/HomeCompanion.json</c> on Linux) — per-user overrides</item>
    ///   <item>top-level <c>*.json</c> files in <c>/etc/homecompanion</c> (alphabetical order)</item>
    ///   <item>top-level <c>*.json</c> files in <c>$XDG_CONFIG_HOME/homecompanion</c> and <c>~/.config/homecompanion</c> (alphabetical order)</item>
    /// </list>
    /// All files are optional; missing files are silently skipped.
    /// Changes to these files are picked up at runtime without a restart (<c>reloadOnChange: true</c>).
    /// </remarks>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/> to configure.</param>
    /// <returns>The modified <see cref="IHostApplicationBuilder"/> for chaining.</returns>
    public static IHostApplicationBuilder AddHomeCompanionConfiguration(this IHostApplicationBuilder builder)
    {
        var configuredConfigDirectories = ResolveConfiguredConfigDirectories(builder.Configuration, builder.Environment.ContentRootPath);
        var extraPaths = ResolveHomeCompanionJsonConfigurationPaths(configuredConfigDirectories: configuredConfigDirectories);
        var extraPathSet = new HashSet<string>(extraPaths, StringComparer.OrdinalIgnoreCase);

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
                if (!string.IsNullOrWhiteSpace(path) && extraPathSet.Contains(NormalizePath(path)))
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

    internal static IReadOnlyList<string> ResolveConfiguredConfigDirectories(IConfiguration configuration, string contentRootPath)
    {
        var directories = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddDirectory(string? directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                return;

            var normalized = NormalizePathWithBase(directoryPath, contentRootPath);
            if (seen.Add(normalized))
                directories.Add(normalized);
        }

        // Convention-based local development directory (works with HomeCompanion.Local/Server content root).
        //AddDirectory(Path.Combine(contentRootPath, "..", "Config")); // use appsettings for this instead --- IGNORE ---

        var configuredSingleDirectory = configuration[$"{AppName}:{nameof(CoreOptions.ConfigDirectory)}"];
        AddDirectory(configuredSingleDirectory);

        var configuredDirectoryArray = configuration.GetSection($"{AppName}:{nameof(CoreOptions.ConfigDirectories)}").Get<string[]>();
        if (configuredDirectoryArray is not null)
        {
            foreach (var directory in configuredDirectoryArray)
                AddDirectory(directory);
        }

        var configuredDirectoryList = configuration[$"{AppName}:{nameof(CoreOptions.ConfigDirectories)}"];
        if (!string.IsNullOrWhiteSpace(configuredDirectoryList))
        {
            foreach (var directory in configuredDirectoryList.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                AddDirectory(directory);
        }

        return directories;
    }

    internal static IReadOnlyList<string> ResolveHomeCompanionJsonConfigurationPaths(
        Func<string, bool>? directoryExists = null,
        Func<string, IEnumerable<string>>? enumerateFiles = null,
        Func<string?>? xdgConfigHomeAccessor = null,
        Func<string>? appDataPathAccessor = null,
        Func<string?>? homePathAccessor = null,
        IReadOnlyList<string>? configuredConfigDirectories = null)
    {
        directoryExists ??= Directory.Exists;
        enumerateFiles ??= path => Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly);
        xdgConfigHomeAccessor ??= () => Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        appDataPathAccessor ??= () => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        homePathAccessor ??= () => Environment.GetEnvironmentVariable("HOME");

        var userConfigRoot = ResolveUserConfigRoot(xdgConfigHomeAccessor, appDataPathAccessor, homePathAccessor);
        var homeDotConfig = ResolveHomeDotConfigRoot(homePathAccessor);

        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var normalized = NormalizePath(path);
            if (seen.Add(normalized))
                paths.Add(normalized);
        }

        // Legacy single-file locations are kept for backwards compatibility.
        AddPath(Path.Combine("/etc", $"{AppName}.json"));
        AddPath(Path.Combine("/etc/homecompanion", $"{AppName}.json"));
        AddPath(Path.Combine(userConfigRoot, $"{AppName}.json"));
        AddPath(Path.Combine(homeDotConfig, $"{AppName}.json"));

        var directories = new List<string>
        {
            Path.Combine("/etc", "homecompanion"),
            Path.Combine(userConfigRoot, "homecompanion"),
            Path.Combine(homeDotConfig, "homecompanion"),
        };

        if (configuredConfigDirectories is not null)
        {
            directories.AddRange(configuredConfigDirectories);
        }

        foreach (var directoryPath in directories)
        {
            foreach (var filePath in EnumerateJsonFilesInDirectory(directoryPath, directoryExists, enumerateFiles))
                AddPath(filePath);
        }

        return paths;
    }

    internal static IReadOnlyList<string> EnumerateJsonFilesInDirectory(
        string directoryPath,
        Func<string, bool> directoryExists,
        Func<string, IEnumerable<string>> enumerateFiles)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !directoryExists(directoryPath))
            return [];

        return enumerateFiles(directoryPath)
            .Where(path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            .Select(NormalizePath)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string ResolveUserConfigRoot(
        Func<string?> xdgConfigHomeAccessor,
        Func<string> appDataPathAccessor,
        Func<string?> homePathAccessor)
    {
        var xdgConfigHome = xdgConfigHomeAccessor();
        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
            return NormalizePath(xdgConfigHome);

        var appDataPath = appDataPathAccessor();
        if (!string.IsNullOrWhiteSpace(appDataPath))
            return NormalizePath(appDataPath);

        var homePath = homePathAccessor();
        if (!string.IsNullOrWhiteSpace(homePath))
            return NormalizePath(Path.Combine(homePath, ".config"));

        return NormalizePath(Path.Combine("~", ".config"));
    }

    internal static string ResolveHomeDotConfigRoot(Func<string?> homePathAccessor)
    {
        var homePath = homePathAccessor();
        if (!string.IsNullOrWhiteSpace(homePath))
            return NormalizePath(Path.Combine(homePath, ".config"));

        return NormalizePath(Path.Combine("~", ".config"));
    }

    internal static string NormalizePath(string path)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        return path;
    }

    internal static string NormalizePathWithBase(string path, string basePath)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        return Path.IsPathRooted(path)
            ? NormalizePath(path)
            : NormalizePath(Path.Combine(basePath, path));
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

    private static string ResolvePathRelativeToAppBase(string configuredPath, string fallback)
    {
        var effectivePath = string.IsNullOrWhiteSpace(configuredPath) ? fallback : configuredPath;
        return Path.IsPathRooted(effectivePath)
            ? effectivePath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, effectivePath));
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

        // Create a temporary service provider to resolve dependencies for extension constructors.
        // ensure configuration is available for extensions that require it
        builder.Services.AddOptions();
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
    /// Types marked with <see cref="ManualConnectivityProviderRegistrationAttribute"/> are excluded from this automatic registration path.
    /// Each provider is registered as a singleton; the <see cref="IHostedService"/> registration forwards to the
    /// same singleton instance so the host manages the connection lifecycle.
    /// </remarks>
    public static IServiceCollection AddConnectivityProviders(this IServiceCollection services)
    {
        var providerInterface = typeof(IConnectivityProvider);

        var providerTypes = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(GetExportedTypesSafe)
            .Where(t => t.IsClass && !t.IsAbstract && providerInterface.IsAssignableFrom(t))
            .Where(t => t.GetCustomAttributes(typeof(ManualConnectivityProviderRegistrationAttribute), inherit: false).Length == 0);

        foreach (var type in providerTypes)
        {
            services.AddSingleton(type);
            services.AddSingleton(providerInterface, sp => sp.GetRequiredService(type));
            services.AddSingleton(typeof(IHostedService), sp =>
            {
                var inner = (IHostedService)sp.GetRequiredService(type);
                var loggerFactory = sp.GetService<ILoggerFactory>();
                var logger = loggerFactory?.CreateLogger($"ConnectivityProviderHost[{type.Name}]");
                return new DeferredStartHostedService(inner, logger);
            });
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
        => AddLogics(services, environmentName: null, configuredEnvironmentRules: null);

    internal static IServiceCollection AddLogics(
        this IServiceCollection services,
        string? environmentName,
        IReadOnlyDictionary<string, string[]>? configuredEnvironmentRules)
    {
        var logicInterface = typeof(ILogic);

        var logicTypes = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(GetExportedTypesSafe)
            .Where(t => t.IsClass && !t.IsAbstract && logicInterface.IsAssignableFrom(t));

        var discoveredCount = 0;
        var registeredCount = 0;
        var skippedCount = 0;

        foreach (var type in logicTypes)
        {
            discoveredCount++;

            if (!ShouldRegisterLogicType(type, environmentName, configuredEnvironmentRules, out var reason))
            {
                skippedCount++;

                if (!string.IsNullOrWhiteSpace(environmentName))
                {
                    Console.Error.WriteLine(
                        $"Skipping logic registration: {type.FullName ?? type.Name} in environment '{environmentName}'. {reason}");
                }

                continue;
            }

            RegisterLogicType(services, type);
            registeredCount++;
        }

        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            Console.Error.WriteLine(
                $"Logic discovery summary for environment '{environmentName}': discovered={discoveredCount}, registered={registeredCount}, skipped={skippedCount}.");
        }

        return services;
    }

    internal static bool ShouldRegisterLogicType(
        Type logicType,
        string? environmentName,
        IReadOnlyDictionary<string, string[]>? configuredEnvironmentRules,
        out string reason)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            reason = "No environment name provided; logic registration is unrestricted.";
            return true;
        }

        var currentEnvironment = environmentName.Trim();
        var allowedEnvironments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var hasAttributeRule = false;
        var attribute = Attribute.GetCustomAttribute(logicType, typeof(LoadInEnvironmentsAttribute), false) as LoadInEnvironmentsAttribute;
        if (attribute is not null)
        {
            hasAttributeRule = true;
            allowedEnvironments.UnionWith(NormalizeEnvironmentNames(attribute.Environments));
        }

        var hasConfigurationRule = TryGetConfiguredLogicEnvironments(
            logicType,
            configuredEnvironmentRules,
            out var configuredEnvironments,
            out var matchedConfigurationKey);

        if (hasConfigurationRule)
            allowedEnvironments.UnionWith(NormalizeEnvironmentNames(configuredEnvironments));

        if (!hasAttributeRule && !hasConfigurationRule)
        {
            reason = "No attribute or configuration rule defined; logic registration is unrestricted.";
            return true;
        }

        var allowedEnvironmentList = allowedEnvironments.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var sourceText = hasConfigurationRule
            ? $"attribute + configuration(key='{matchedConfigurationKey}')"
            : "attribute";

        if (!hasAttributeRule && hasConfigurationRule)
            sourceText = $"configuration(key='{matchedConfigurationKey}')";

        if (allowedEnvironmentList.Length == 0)
        {
            reason = $"Rule source: {sourceText}; effective environment set is empty.";
            return false;
        }

        var isAllowed = allowedEnvironments.Contains(currentEnvironment);
        reason = $"Rule source: {sourceText}; allowed environments: [{string.Join(", ", allowedEnvironmentList)}].";
        return isAllowed;
    }

    internal static IReadOnlyDictionary<string, string[]> ResolveLogicEnvironmentRules(IConfiguration configuration)
    {
        var configuredRules = configuration
            .GetSection(AppName)
            .GetSection(nameof(CoreOptions.LogicEnvironmentRules))
            .Get<Dictionary<string, string[]>>()
            ?? [];

        return new Dictionary<string, string[]>(configuredRules, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryGetConfiguredLogicEnvironments(
        Type logicType,
        IReadOnlyDictionary<string, string[]>? configuredEnvironmentRules,
        out string[] configuredEnvironments,
        out string? matchedConfigurationKey)
    {
        configuredEnvironments = [];
        matchedConfigurationKey = null;

        if (configuredEnvironmentRules is null || configuredEnvironmentRules.Count == 0)
            return false;

        if (!string.IsNullOrWhiteSpace(logicType.FullName)
            && configuredEnvironmentRules.TryGetValue(logicType.FullName, out var fullNameEnvironments))
        {
            var normalized = NormalizeEnvironmentNames(fullNameEnvironments).ToArray();
            if (normalized.Length > 0)
            {
                configuredEnvironments = normalized;
                matchedConfigurationKey = logicType.FullName;
                return true;
            }
        }

        if (configuredEnvironmentRules.TryGetValue(logicType.Name, out var typeNameEnvironments))
        {
            var normalized = NormalizeEnvironmentNames(typeNameEnvironments).ToArray();
            if (normalized.Length > 0)
            {
                configuredEnvironments = normalized;
                matchedConfigurationKey = logicType.Name;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> NormalizeEnvironmentNames(IEnumerable<string>? environmentNames)
        => environmentNames is null
            ? []
            : environmentNames
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim());

    internal static void RegisterLogicType(IServiceCollection services, Type type)
    {
        services.TryAddSingleton(type);
        services.AddSingleton(typeof(ILogic), sp => (ILogic)sp.GetRequiredService(type));

        if (typeof(IDiagnosable).IsAssignableFrom(type))
        {
            services.AddSingleton(typeof(IDiagnosable), sp => (IDiagnosable)sp.GetRequiredService(type));
        }
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

    private sealed class DeferredStartHostedService(IHostedService inner, ILogger? logger) : IHostedService
    {
        private readonly IHostedService _inner = inner;
        private readonly ILogger? _logger = logger;
        private Task? _startTask;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _startTask = Task.Run(async () =>
            {
                try
                {
                    await _inner.StartAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogDebug("Connectivity provider start canceled by host token.");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Connectivity provider failed during asynchronous startup.");
                }
            }, CancellationToken.None);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_startTask is not null)
            {
                try
                {
                    await _startTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogDebug("Host cancellation requested while waiting for provider startup task.");
                }
            }

            await _inner.StopAsync(cancellationToken).ConfigureAwait(false);
        }
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
