using HomeCompanion;
using HomeCompanion.Logics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeCompanion.Core;

/// <summary>
/// Hosted service that initializes all registered <see cref="ILogic"/> instances after connectivity providers
/// are ready, in dependency order with independent trees initialized in parallel.
/// </summary>
/// <remarks>
/// Waits until every registered <see cref="IConnectivityProvider"/> reports both <c>IsConnected</c>
/// and <c>IsInitializationFinished</c>, or until <see cref="CoreOptions.BusInitializationTimeout"/> elapses,
/// whichever comes first. If no providers are registered, initialization proceeds immediately.
/// <para>
/// Initialization order is derived from constructor-injected <see cref="ILogic"/> parameters: a logic
/// that receives another logic via its constructor is considered to depend on it and will be initialized
/// after it. Independent logics (or logics whose dependencies are all satisfied within the same level) are
/// initialized concurrently via <see cref="Task.WhenAll(IEnumerable{Task})"/>.
/// </para>
/// <para>
/// A cyclic dependency graph is detected before any <c>InitializeAsync</c> call and causes an
/// <see cref="InvalidOperationException"/> to be thrown, failing the hosted service startup.
/// </para>
/// </remarks>
internal sealed class LogicManager : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly IReadOnlyList<IConnectivityProvider> _providers;
    private readonly IReadOnlyList<ILogic> _logics;
    private readonly CoreOptions _options;
    private readonly ILogger<LogicManager> _logger;
    private readonly TimeProvider _timeProvider;

    public LogicManager(
        IEnumerable<IConnectivityProvider> providers,
        IEnumerable<ILogic> logics,
        IOptions<CoreOptions> options,
        ILogger<LogicManager> logger,
        TimeProvider timeProvider)
    {
        _providers = providers.ToList();
        _logics = logics.ToList();
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_logics.Count == 0)
        {
            _logger.LogDebug("No ILogic instances registered; skipping initialization.");
            return;
        }

        await WaitForProvidersAsync(stoppingToken);

        if (stoppingToken.IsCancellationRequested)
            return;

        var levels = BuildInitializationLevels(_logics);
        await InitializeLevelsAsync(levels, stoppingToken);
    }

    private async Task WaitForProvidersAsync(CancellationToken stoppingToken)
    {
        if (_providers.Count == 0)
        {
            _logger.LogDebug("No IConnectivityProvider instances registered; proceeding with logic initialization immediately.");
            return;
        }

        using var timeoutCts = new CancellationTokenSource(_options.BusInitializationTimeout, _timeProvider);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);

        while (!AllProvidersReady())
        {
            try
            {
                await Task.Delay(PollInterval, _timeProvider, linkedCts.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Timeout fired (not host shutdown) — warn and proceed.
                _logger.LogWarning(
                    "Timed out after {Timeout} waiting for all connectivity providers to become ready. Proceeding with logic initialization.",
                    _options.BusInitializationTimeout);
                return;
            }
            catch (OperationCanceledException)
            {
                // Host is shutting down — abort.
                return;
            }
        }

        _logger.LogDebug("All connectivity providers are ready.");
    }

    private bool AllProvidersReady()
        => _providers.All(p => p.IsConnected && p.IsInitializationFinished);

    /// <summary>
    /// Builds initialization levels using Kahn's topological sort, mapping type levels back to logic instances.
    /// Throws <see cref="InvalidOperationException"/> if a dependency cycle is detected.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<ILogic>> BuildInitializationLevels(IReadOnlyList<ILogic> logics)
    {
        var byType = logics.ToDictionary(l => l.GetType());
        var typeLevels = BuildInitializationLevelsFromTypes(logics.Select(l => l.GetType()).ToList());
        return typeLevels
            .Select(level => (IReadOnlyList<ILogic>)level.Select(t => byType[t]).ToList())
            .ToList();
    }

    /// <summary>
    /// Builds initialization levels from logic types using Kahn's topological sort.
    /// Dependencies are inferred from each type's constructor: parameters whose type implements
    /// <see cref="ILogic"/> and appears in <paramref name="logicTypes"/> are treated as dependency edges.
    /// </summary>
    /// <remarks>
    /// Each returned level contains types whose dependencies have all been satisfied by prior levels.
    /// Types within the same level are independent of each other and may be initialized in parallel.
    /// This method is also used for static dependency analysis (e.g. in tests).
    /// </remarks>
    /// <exception cref="InvalidOperationException">A cyclic dependency is detected.</exception>
    internal static IReadOnlyList<IReadOnlyList<Type>> BuildInitializationLevelsFromTypes(IReadOnlyList<Type> logicTypes)
    {
        var typeSet = logicTypes.ToHashSet();
        var logicInterface = typeof(ILogic);

        // For each type, collect the ILogic-typed constructor parameters present in the set.
        var deps = logicTypes.ToDictionary(t => t, t =>
        {
            var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            if (ctor is null) return (IReadOnlyList<Type>)[];
            return (IReadOnlyList<Type>)ctor.GetParameters()
                .Select(p => p.ParameterType)
                .Where(pt => logicInterface.IsAssignableFrom(pt) && typeSet.Contains(pt))
                .ToList();
        });

        // In-degree = number of unmet dependencies for each node.
        var inDegree = logicTypes.ToDictionary(t => t, t => deps[t].Count);

        // Reverse adjacency: dep -> types that depend on it.
        var dependents = logicTypes.ToDictionary(t => t, _ => new List<Type>());
        foreach (var (type, typeDeps) in deps)
            foreach (var dep in typeDeps)
                dependents[dep].Add(type);

        var levels = new List<IReadOnlyList<Type>>();
        var remaining = new HashSet<Type>(logicTypes);

        while (remaining.Count > 0)
        {
            var level = remaining.Where(t => inDegree[t] == 0).ToList();

            if (level.Count == 0)
            {
                var cycleParticipants = string.Join(", ", remaining.Select(t => t.Name));
                throw new InvalidOperationException(
                    $"Cyclic dependency detected among ILogic implementations: {cycleParticipants}");
            }

            levels.Add(level);

            foreach (var type in level)
            {
                remaining.Remove(type);
                foreach (var dependent in dependents[type])
                    inDegree[dependent]--;
            }
        }

        return levels;
    }

    private async Task InitializeLevelsAsync(IReadOnlyList<IReadOnlyList<ILogic>> levels, CancellationToken cancellationToken)
    {
        for (int i = 0; i < levels.Count; i++)
        {
            var level = levels[i];
            _logger.LogDebug("Initializing logic level {Level}/{Total} ({Count} logic(s)).", i + 1, levels.Count, level.Count);
            await Task.WhenAll(level.Select(l => InitializeSafeAsync(l, cancellationToken)));
        }
    }

    private async Task InitializeSafeAsync(ILogic logic, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Initializing {LogicType}.", logic.GetType().Name);
            await logic.InitializeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception initializing {LogicType}.", logic.GetType().Name);
        }
    }
}
