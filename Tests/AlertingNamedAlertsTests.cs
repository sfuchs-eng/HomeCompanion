using HomeCompanion.Alerting;
using HomeCompanion.Events;
using HomeCompanion.Integrations.Alerting.Named;
using HomeCompanion.Integrations.Alerting.Values;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests;

public class AlertingNamedAlertsTests
{
    [Test]
    public void NamedAlertStateMachine_Transitions_AreAppliedAsSpecified()
    {
        var machine = new NamedAlertStateMachine();
        var now = new DateTimeOffset(2026, 5, 25, 10, 0, 0, TimeSpan.Zero);
        var interval = TimeSpan.FromMinutes(5);

        var trigger = machine.ApplyIntent(
            new NamedAlertIntent { AlertKey = "Smoke.Alarm", IntentType = NamedAlertIntentType.Trigger, Message = "Smoke detected" },
            now,
            interval);

        Assert.That(trigger.PreviousStatus, Is.EqualTo(NamedAlertStatus.Monitoring));
        Assert.That(trigger.CurrentStatus, Is.EqualTo(NamedAlertStatus.Alert));
        Assert.That(trigger.State.NextReminderDueUtc, Is.EqualTo(now + interval));

        var acknowledge = machine.ApplyIntent(
            new NamedAlertIntent { AlertKey = "Smoke.Alarm", IntentType = NamedAlertIntentType.Acknowledge },
            now.AddMinutes(1),
            interval);

        Assert.That(acknowledge.CurrentStatus, Is.EqualTo(NamedAlertStatus.Acknowledged));

        var reset = machine.ApplyIntent(
            new NamedAlertIntent { AlertKey = "Smoke.Alarm", IntentType = NamedAlertIntentType.Reset },
            now.AddMinutes(2),
            interval);

        Assert.That(reset.CurrentStatus, Is.EqualTo(NamedAlertStatus.Monitoring));

        var disable = machine.ApplyIntent(
            new NamedAlertIntent { AlertKey = "Smoke.Alarm", IntentType = NamedAlertIntentType.Disable },
            now.AddMinutes(3),
            interval);

        Assert.That(disable.CurrentStatus, Is.EqualTo(NamedAlertStatus.Disabled));

        var triggerWhileDisabled = machine.ApplyIntent(
            new NamedAlertIntent { AlertKey = "Smoke.Alarm", IntentType = NamedAlertIntentType.Trigger },
            now.AddMinutes(4),
            interval);

        Assert.That(triggerWhileDisabled.CurrentStatus, Is.EqualTo(NamedAlertStatus.Disabled));
        Assert.That(triggerWhileDisabled.StateChanged, Is.False);

        var enable = machine.ApplyIntent(
            new NamedAlertIntent { AlertKey = "Smoke.Alarm", IntentType = NamedAlertIntentType.Enable },
            now.AddMinutes(5),
            interval);

        Assert.That(enable.CurrentStatus, Is.EqualTo(NamedAlertStatus.Monitoring));
    }

    [Test]
    public void AlertingValues_WriteToAcknowledgeAndDisable_MapsToStateMachineIntents()
    {
        var machine = new NamedAlertStateMachine();
        var publisher = new NoopEventPublisher();
        var valuesManager = new TestValuesManager();
        var loggerFactory = NullLoggerFactory.Instance;
        var alertingValues = new AlertingValues(
            NullLogger<ValueContainerBase>.Instance,
            loggerFactory,
            publisher,
            valuesManager,
            machine,
            TimeProvider.System,
            NullLogger<AlertingValues>.Instance);

        machine.ApplyIntent(
            new NamedAlertIntent { AlertKey = "Window.OpenTooLong", IntentType = NamedAlertIntentType.Trigger, Message = "Window open" },
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(15));

        var allValues = alertingValues.GetValues().ToArray();
        var ackValue = allValues.OfType<IValue<bool>>().Single(v => string.Equals(v.Name, "Alerting_Window_OpenTooLong_Acknowledge", StringComparison.Ordinal));
        var disabledValue = allValues.OfType<IValue<bool>>().Single(v => string.Equals(v.Name, "Alerting_Window_OpenTooLong_Disabled", StringComparison.Ordinal));

        ackValue.Write(true);

        var acknowledged = machine.GetSnapshot().Single(s => s.AlertKey == "Window.OpenTooLong");
        Assert.That(acknowledged.Status, Is.EqualTo(NamedAlertStatus.Acknowledged));

        disabledValue.Write(true);

        var disabled = machine.GetSnapshot().Single(s => s.AlertKey == "Window.OpenTooLong");
        Assert.That(disabled.Status, Is.EqualTo(NamedAlertStatus.Disabled));

        disabledValue.Write(false);

        var enabled = machine.GetSnapshot().Single(s => s.AlertKey == "Window.OpenTooLong");
        Assert.That(enabled.Status, Is.EqualTo(NamedAlertStatus.Monitoring));
    }

    private sealed class NoopEventPublisher : IEventPublisher
    {
        public ValueTask PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class TestValuesManager : IValuesManager
    {
        private readonly HashSet<IValue> _registered = [];

        public void RegisterValue(IValue value) => _registered.Add(value);

        public void UnregisterValue(IValue value) => _registered.Remove(value);
    }
}
