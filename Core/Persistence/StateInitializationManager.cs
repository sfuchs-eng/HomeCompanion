using HomeCompanion.Abstractions;
using HomeCompanion.Persistence;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeCompanion.Core.Persistence;

public class StateInitializationManager : IStateInitializationManager
{
    private const string ValueSnapshotStateSetName = "value-snapshot";
    private static readonly TimeSpan ValueSnapshotMaxAge = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        AllowTrailingCommas = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
            | System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() },
    };

    protected readonly ILogger<StateInitializationManager> Logger;
    private readonly IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization;
    protected readonly IStateStore StateStore;
    private readonly IEnumerable<IValuesContainer> _valuesContainers;
    private readonly TimeProvider _timeProvider;

    private readonly Dictionary<AppInitializationStage, List<StateInitializationDelegate>> Initializations;
    private readonly object _initializationLock = new();

    public StateInitializationManager(
            IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization,
            IStateStore stateStore,
            IEnumerable<IValuesContainer> valuesContainers,
            ILogger<StateInitializationManager> logger,
            TimeProvider? timeProvider = null
    )
    {
        Logger = logger;
        this.lifeCycleSynchronization = lifeCycleSynchronization;
        StateStore = stateStore;
        _valuesContainers = valuesContainers;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Initializations = Enum.GetValues<AppInitializationStage>()
            .ToDictionary(stage => stage, stage => new List<StateInitializationDelegate>());
        Initializations[AppInitializationStage.InitLoadFromStore].Add(InitializeValuesFromStoreAsync);
        Initializations[AppInitializationStage.ShutDownSave].Add(SaveValuesStateAsync);
        lifeCycleSynchronization.SignalInitializationStageCompletedAsync(AppInitializationStage.Default).GetAwaiter().GetResult();
    }

    public void RegisterInitialization(AppInitializationStage stage, StateInitializationDelegate initialization)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        lock (_initializationLock)
        {
            Initializations[stage].Add(initialization);
        }
    }

    public void RemoveInitialization(AppInitializationStage stage, StateInitializationDelegate initialization)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        lock (_initializationLock)
        {
            Initializations[stage].Remove(initialization);
        }
    }

    protected virtual async Task ExecuteInitializationDelegatesAsync(IAsyncEnumerable<StateInitializationDelegate> initializations, CancellationToken token = default)
    {
        await foreach (var initialization in initializations.WithCancellation(token).ConfigureAwait(false))
        {
            try
            {
                Logger.LogInformation("Executing {DeclaringType}.{MethodName} for stage {Stage}", initialization.Method.DeclaringType?.Name, initialization.Method.Name, CurrentStage);
                await initialization(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected when the cancellation token is triggered, no action needed
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
            {
                // expected when the task is canceled, no action needed
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Unhandled failure during invocation of {DeclaringType}.{MethodName} for stage {Stage}.", initialization.Method.DeclaringType?.Name, initialization.Method.Name, CurrentStage);
            }
        }
    }

    /// <summary>
    /// Trigger initialization
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task InitializeStateAsync(CancellationToken token = default)
    {
        Logger.LogInformation("Starting values initialization.");
        var skipStages = new HashSet<AppInitializationStage>()
        {
            AppInitializationStage.Default,
            AppInitializationStage.ShutDownSave
        };

        foreach (var stage in Enum.GetValues<AppInitializationStage>())
        {
            if ( skipStages.Contains(stage)) continue;
            if (Initializations[stage].Count == 0)
            {
                Logger.LogTrace("Skipping stage {Stage} as it has no registered initialization delegates.", stage);
                continue;
            }

            await ExecuteInitializationDelegatesAsync(Initializations[stage].ToAsyncEnumerable(), token).ConfigureAwait(false);
            CurrentStage = stage;
            await lifeCycleSynchronization.SignalInitializationStageCompletedAsync(stage).ConfigureAwait(false);
            Logger.LogInformation("Completed state initialization stage {Stage}.", stage);
        }
    }

    /// <summary>
    /// Trigger saving
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task SaveStateAsync(CancellationToken token = default)
    {
        await ExecuteInitializationDelegatesAsync(Initializations[AppInitializationStage.ShutDownSave].ToAsyncEnumerable(), token).ConfigureAwait(false);
        await lifeCycleSynchronization.SignalInitializationStageCompletedAsync(AppInitializationStage.ShutDownSave);
    }

    protected virtual async Task InitializeValuesFromStoreAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        StateLoadingResult<ValueSnapshotSet> stateLoadingResult;
        try
        {
            stateLoadingResult = await StateStore.LoadAsync<ValueSnapshotSet>(ValueSnapshotStateSetName, ValueSnapshotMaxAge).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed loading value snapshot state set '{StateSetName}'.", ValueSnapshotStateSetName);
            return;
        }

        if (!stateLoadingResult.IsSuccess)
        {
            Logger.LogInformation("No recent value snapshot available from state set '{StateSetName}'.", ValueSnapshotStateSetName);
            return;
        }

        if (!stateLoadingResult.IsRecent)
        {
            Logger.LogInformation("Skipping stale value snapshot from state set '{StateSetName}'.", ValueSnapshotStateSetName);
            return;
        }

        var snapshot = stateLoadingResult.StateSet;
        if (snapshot.Values.Count == 0)
        {
            Logger.LogTrace("Value snapshot state set '{StateSetName}' is empty.", ValueSnapshotStateSetName);
            return;
        }

        var byName = snapshot.Values.Values
            .Where(v => !string.IsNullOrWhiteSpace(v.ValueName))
            .GroupBy(v => v.ValueName!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var discovered = 0;
        var restored = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var binding in GetValueBindings())
        {
            token.ThrowIfCancellationRequested();
            discovered++;

            if (!snapshot.Values.TryGetValue(binding.Key, out var stateEntry)
                && (string.IsNullOrWhiteSpace(binding.Value.Name) || !byName.TryGetValue(binding.Value.Name, out stateEntry)))
            {
                skipped++;
                continue;
            }

            try
            {
                var deserialized = JsonSerializer.Deserialize(stateEntry.PayloadJson, binding.Value.ValueType, SnapshotJsonOptions);
                if (!binding.Value.InitializeValue(deserialized!, AppInitializationStage.InitLoadFromStore))
                {
                    failed++;
                    Logger.LogWarning(
                        "Value snapshot restore was rejected for key '{ValueKey}' ({Container}.{PropertyName}).",
                        binding.Key,
                        binding.ContainerTypeName,
                        binding.PropertyName);
                    continue;
                }

                restored++;
            }
            catch (Exception ex)
            {
                failed++;
                Logger.LogWarning(
                    ex,
                    "Failed restoring value snapshot for key '{ValueKey}' ({Container}.{PropertyName}).",
                    binding.Key,
                    binding.ContainerTypeName,
                    binding.PropertyName);
            }
        }

        Logger.LogInformation(
            "Value snapshot restore summary: discovered={Discovered}, restored={Restored}, skipped={Skipped}, failed={Failed}.",
            discovered,
            restored,
            skipped,
            failed);
    }

    protected virtual async Task SaveValuesStateAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var snapshot = new ValueSnapshotSet
        {
            Version = 1,
            CreatedUtc = _timeProvider.GetUtcNow(),
        };

        var discovered = 0;
        var saved = 0;
        var failed = 0;

        foreach (var binding in GetValueBindings())
        {
            token.ThrowIfCancellationRequested();
            discovered++;

            if (!TryReadCurrentValue(binding.Value, out var currentValue))
            {
                failed++;
                Logger.LogWarning(
                    "Skipping value snapshot for key '{ValueKey}' ({Container}.{PropertyName}) because current value could not be read.",
                    binding.Key,
                    binding.ContainerTypeName,
                    binding.PropertyName);
                continue;
            }

            try
            {
                var payloadJson = JsonSerializer.Serialize(currentValue, binding.Value.ValueType, SnapshotJsonOptions);

                if (!snapshot.Values.TryAdd(binding.Key, new ValueSnapshotEntry
                {
                    Key = binding.Key,
                    ValueName = binding.Value.Name,
                    ValueLabel = binding.Value.Label,
                    ValueType = binding.Value.ValueType.AssemblyQualifiedName,
                    PayloadJson = payloadJson,
                    CapturedUtc = _timeProvider.GetUtcNow(),
                }))
                {
                    Logger.LogWarning(
                        "Duplicate value snapshot key '{ValueKey}' detected for {Container}.{PropertyName}; first entry kept.",
                        binding.Key,
                        binding.ContainerTypeName,
                        binding.PropertyName);
                    continue;
                }

                saved++;
            }
            catch (Exception ex)
            {
                failed++;
                Logger.LogWarning(
                    ex,
                    "Failed creating value snapshot for key '{ValueKey}' ({Container}.{PropertyName}).",
                    binding.Key,
                    binding.ContainerTypeName,
                    binding.PropertyName);
            }
        }

        await StateStore.SaveAsync(ValueSnapshotStateSetName, snapshot, token).ConfigureAwait(false);

        Logger.LogInformation(
            "Value snapshot save summary: discovered={Discovered}, saved={Saved}, failed={Failed}.",
            discovered,
            saved,
            failed);
    }

    private static bool TryReadCurrentValue(IValue value, out object? currentValue)
    {
        var valueProperty = value.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        if (valueProperty is null || !valueProperty.CanRead)
        {
            currentValue = null;
            return false;
        }

        currentValue = valueProperty.GetValue(value);
        return true;
    }

    private IEnumerable<ValueBinding> GetValueBindings()
    {
        foreach (var container in _valuesContainers)
        {
            var containerType = container.GetType();
            var containerTypeName = containerType.FullName ?? containerType.Name;

            foreach (var property in containerType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!typeof(IValue).IsAssignableFrom(property.PropertyType) || !property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;

                if (property.GetValue(container) is not IValue value)
                    continue;

                var key = $"{containerTypeName}|{property.Name}";
                yield return new ValueBinding(key, containerTypeName, property.Name, value);
            }
        }
    }

    private sealed record ValueBinding(string Key, string ContainerTypeName, string PropertyName, IValue Value);

    public void RegisterSave(StateInitializationDelegate save)
    {
        ArgumentNullException.ThrowIfNull(save);
        lock (_initializationLock)
        {
            Initializations[AppInitializationStage.ShutDownSave].Add(save);
        }
    }

    public void RemoveSave(StateInitializationDelegate save)
    {
        ArgumentNullException.ThrowIfNull(save);
        lock (_initializationLock)
        {
            Initializations[AppInitializationStage.ShutDownSave].Remove(save);
        }
    }

    public AppInitializationStage CurrentStage { get; private set; }
}
