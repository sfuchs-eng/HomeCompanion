using HomeCompanion.Alerting;

namespace HomeCompanion.Integrations.Alerting.Named;

/// <summary>
/// In-memory state machine for named alerts.
/// </summary>
public sealed class NamedAlertStateMachine
{
    private readonly Dictionary<string, NamedAlertState> _states = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    /// <summary>
    /// Raised when a named alert state transition is applied.
    /// </summary>
    public event EventHandler<NamedAlertTransitionResult>? StateChanged;

    /// <summary>
    /// Applies a lifecycle intent to one named alert.
    /// </summary>
    /// <param name="intent">Lifecycle intent.</param>
    /// <param name="nowUtc">Current timestamp.</param>
    /// <param name="reminderInterval">Reminder interval used when state becomes alert.</param>
    /// <returns>Transition result.</returns>
    public NamedAlertTransitionResult ApplyIntent(NamedAlertIntent intent, DateTimeOffset nowUtc, TimeSpan reminderInterval)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (string.IsNullOrWhiteSpace(intent.AlertKey))
            throw new ArgumentException("Alert key must not be empty.", nameof(intent));

        NamedAlertTransitionResult result;
        lock (_sync)
        {
            var current = _states.TryGetValue(intent.AlertKey, out var existing)
                ? existing
                : new NamedAlertState(intent.AlertKey, NamedAlertStatus.Monitoring, nowUtc, null, null);

            var previous = current.Status;
            var nextStatus = ResolveNextStatus(current.Status, intent.IntentType);

            DateTimeOffset? nextReminder = nextStatus == NamedAlertStatus.Alert && reminderInterval > TimeSpan.Zero
                ? nowUtc + reminderInterval
                : null;

            var nextState = current with
            {
                Status = nextStatus,
                LastChangeUtc = previous == nextStatus ? current.LastChangeUtc : nowUtc,
                LastMessage = intent.Message ?? current.LastMessage,
                NextReminderDueUtc = nextReminder,
            };

            _states[intent.AlertKey] = nextState;

            result = new NamedAlertTransitionResult
            {
                AlertKey = intent.AlertKey,
                PreviousStatus = previous,
                CurrentStatus = nextStatus,
                StateChanged = previous != nextStatus,
                IntentType = intent.IntentType,
                State = nextState,
            };
        }

        StateChanged?.Invoke(this, result);
        return result;
    }

    /// <summary>
    /// Returns a current snapshot of all named alerts.
    /// </summary>
    /// <returns>Snapshot list.</returns>
    public IReadOnlyCollection<NamedAlertState> GetSnapshot()
    {
        lock (_sync)
        {
            return _states.Values.ToArray();
        }
    }

    /// <summary>
    /// Restores named-alert states from persisted data.
    /// </summary>
    /// <param name="states">Persisted states.</param>
    public void Restore(IEnumerable<NamedAlertState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        lock (_sync)
        {
            foreach (var state in states)
            {
                if (string.IsNullOrWhiteSpace(state.AlertKey))
                    continue;
                _states[state.AlertKey] = state;
            }
        }
    }

    /// <summary>
    /// Returns alert keys whose reminder due timestamp has passed.
    /// </summary>
    /// <param name="nowUtc">Current timestamp.</param>
    /// <returns>Due alert keys.</returns>
    public IReadOnlyList<string> GetReminderDueAlertKeys(DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            return _states.Values
                .Where(s => s.Status == NamedAlertStatus.Alert && s.NextReminderDueUtc is not null && s.NextReminderDueUtc <= nowUtc)
                .Select(s => s.AlertKey)
                .ToArray();
        }
    }

    /// <summary>
    /// Marks one alert reminder as dispatched and computes next due timestamp.
    /// </summary>
    /// <param name="alertKey">Named-alert key.</param>
    /// <param name="nowUtc">Current timestamp.</param>
    /// <param name="reminderInterval">Reminder interval.</param>
    /// <returns>True when update was applied; otherwise false.</returns>
    public bool MarkReminderDispatched(string alertKey, DateTimeOffset nowUtc, TimeSpan reminderInterval)
    {
        if (string.IsNullOrWhiteSpace(alertKey) || reminderInterval <= TimeSpan.Zero)
            return false;

        lock (_sync)
        {
            if (!_states.TryGetValue(alertKey, out var current) || current.Status != NamedAlertStatus.Alert)
                return false;

            _states[alertKey] = current with { NextReminderDueUtc = nowUtc + reminderInterval };
            return true;
        }
    }

    private static NamedAlertStatus ResolveNextStatus(NamedAlertStatus current, NamedAlertIntentType intent)
        => intent switch
        {
            NamedAlertIntentType.Trigger => current == NamedAlertStatus.Disabled ? NamedAlertStatus.Disabled : NamedAlertStatus.Alert,
            NamedAlertIntentType.Reset => current is NamedAlertStatus.Alert or NamedAlertStatus.Acknowledged
                ? NamedAlertStatus.Monitoring
                : current,
            NamedAlertIntentType.Acknowledge => current == NamedAlertStatus.Alert
                ? NamedAlertStatus.Acknowledged
                : current,
            NamedAlertIntentType.Disable => NamedAlertStatus.Disabled,
            NamedAlertIntentType.Enable => current == NamedAlertStatus.Disabled
                ? NamedAlertStatus.Monitoring
                : current,
            _ => current,
        };
}
