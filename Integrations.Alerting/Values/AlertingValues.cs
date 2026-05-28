using HomeCompanion.Alerting;
using HomeCompanion.Abstractions;
using HomeCompanion.Events;
using HomeCompanion.Integrations.Alerting.Named;
using Microsoft.Extensions.Logging;
using HomeCompanion.Values;

namespace HomeCompanion.Integrations.Alerting.Values;

/// <summary>
/// Dynamic values container for named-alert user interaction values.
/// </summary>
public sealed class AlertingValues : ValueContainerBase
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly NamedAlertStateMachine _stateMachine;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AlertingValues> _logger;

    private readonly object _sync = new();
    private readonly Dictionary<string, NamedAlertValueSet> _valuesByAlertKey = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public AlertingValues(
        ILogger<ValueContainerBase> baseLogger,
        ILoggerFactory loggerFactory,
        NamedAlertStateMachine stateMachine,
        TimeProvider timeProvider,
        ILogger<AlertingValues> logger)
        : base(baseLogger)
    {
        _loggerFactory = loggerFactory;
        _stateMachine = stateMachine;
        _timeProvider = timeProvider;
        _logger = logger;

        _stateMachine.StateChanged += OnStateChanged;

        foreach (var state in _stateMachine.GetSnapshot())
            ApplyState(state);
    }

    /// <inheritdoc/>
    public override IEnumerable<IValue> GetValues()
    {
        lock (_sync)
        {
            return _valuesByAlertKey.Values
                .SelectMany(v => v.GetValues())
                .ToArray();
        }
    }

    private void OnStateChanged(object? sender, NamedAlertTransitionResult result)
        => ApplyState(result.State);

    private void ApplyState(NamedAlertState state)
    {
        NamedAlertValueSet valueSet;
        lock (_sync)
        {
            valueSet = GetOrCreateValueSet(state.AlertKey);
        }

        valueSet.Status.InitializeValue(state.Status.ToString(), AppInitializationStage.InitLoadFromStore);
        valueSet.LastChange.InitializeValue(state.LastChangeUtc, AppInitializationStage.InitLoadFromStore);
        valueSet.Disabled.InitializeValue(state.Status == NamedAlertStatus.Disabled, AppInitializationStage.InitLoadFromStore);
    }

    private NamedAlertValueSet GetOrCreateValueSet(string alertKey)
    {
        if (_valuesByAlertKey.TryGetValue(alertKey, out var existing))
            return existing;

        var sanitized = Sanitize(alertKey);
        var prefix = $"Alerting_{sanitized}";

        var ack = CreateValue<bool>(
            name: $"{prefix}_Acknowledge",
            label: $"{alertKey} acknowledge");

        var disabled = CreateValue<bool>(
            name: $"{prefix}_Disabled",
            label: $"{alertKey} disabled");

        var status = CreateValue<string>(
            name: $"{prefix}_Status",
            label: $"{alertKey} status");

        var lastChange = CreateValue<DateTimeOffset>(
            name: $"{prefix}_LastChange",
            label: $"{alertKey} last change");

        ack.InitializeValue(false, AppInitializationStage.Default);
        disabled.InitializeValue(false, AppInitializationStage.Default);
        status.InitializeValue(NamedAlertStatus.Monitoring.ToString(), AppInitializationStage.Default);
        lastChange.InitializeValue(_timeProvider.GetUtcNow(), AppInitializationStage.Default);

        ack.Written += (_, _) => OnAcknowledgeWritten(alertKey, ack);
        disabled.Written += (_, _) => OnDisabledWritten(alertKey, disabled);

        var created = new NamedAlertValueSet(ack, disabled, status, lastChange);
        _valuesByAlertKey[alertKey] = created;

        _logger.LogInformation("Created named-alert values for key '{AlertKey}'.", alertKey);

        return created;
    }

    private ValueBase<T> CreateValue<T>(string name, string label)
    {
        var value = new ValueBase<T>(_loggerFactory.CreateLogger<ValueBase<T>>())
        {
            Name = name,
            Label = label,
        };
        return value;
    }

    private void OnAcknowledgeWritten(string alertKey, ValueBase<bool> acknowledge)
    {
        if (!acknowledge.Value)
            return;

        _stateMachine.ApplyIntent(new NamedAlertIntent
        {
            AlertKey = alertKey,
            IntentType = NamedAlertIntentType.Acknowledge,
            Message = "Acknowledged via value write.",
        }, _timeProvider.GetUtcNow(), TimeSpan.FromMinutes(15));

        acknowledge.InitializeValue(false, AppInitializationStage.InitBusValueReceived);
    }

    private void OnDisabledWritten(string alertKey, ValueBase<bool> disabled)
    {
        _stateMachine.ApplyIntent(new NamedAlertIntent
        {
            AlertKey = alertKey,
            IntentType = disabled.Value ? NamedAlertIntentType.Disable : NamedAlertIntentType.Enable,
            Message = disabled.Value ? "Disabled via value write." : "Enabled via value write.",
        }, _timeProvider.GetUtcNow(), TimeSpan.FromMinutes(15));
    }

    private static string Sanitize(string input)
    {
        var chars = input
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();

        return new string(chars);
    }

    private sealed record NamedAlertValueSet(
        ValueBase<bool> Acknowledge,
        ValueBase<bool> Disabled,
        ValueBase<string> Status,
        ValueBase<DateTimeOffset> LastChange)
    {
        public IEnumerable<IValue> GetValues()
        {
            yield return Acknowledge;
            yield return Disabled;
            yield return Status;
            yield return LastChange;
        }
    }
}
