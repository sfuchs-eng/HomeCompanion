using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Logics.Shutters;

public class ShutterAutomationComputationTriggerContext
{
    public IEnumerable<IThingKey> ThingKeys { get; }
    public ShutterAutomationComputationScope Scope { get; }
    public ShutterAutomationComputationTriggerUrgency Urgency { get; }
    public IEnumerable<IValue>? TriggeringValues { get; }
    public IEnumerable<ValueEventArgs>? ValueEventArgs { get; }
    public DateTimeOffset Timestamp { get; }

    public ShutterAutomationComputationTriggerContext(
        IEnumerable<IThingKey> thingKeys,
        ShutterAutomationComputationScope scope,
        IEnumerable<IValue>? triggeringValue,
        IEnumerable<ValueEventArgs>? valueEventArgs,
        DateTimeOffset timestamp,
        ShutterAutomationComputationTriggerUrgency urgency = ShutterAutomationComputationTriggerUrgency.Normal)
    {
        ThingKeys = thingKeys;
        Scope = scope;
        Urgency = urgency;
        TriggeringValues = triggeringValue;
        ValueEventArgs = valueEventArgs;
        Timestamp = timestamp;
    }

    public ShutterAutomationComputationTriggerContext(IEnumerable<ShutterAutomationComputationTriggerContext> triggerContexts)
    {
        ThingKeys = [.. triggerContexts.SelectMany(tc => tc.ThingKeys)];
        if (ThingKeys.DistinctBy(sk => sk.Key).Count() != ThingKeys.Count())
            throw new ArgumentException("Cannot merge trigger contexts with overlapping shutter keys.", nameof(triggerContexts));
        Scope = triggerContexts.Max(tc => tc.Scope);
        Urgency = triggerContexts.Max(tc => tc.Urgency);
        TriggeringValues = triggerContexts.SelectMany(tc => tc.TriggeringValues ?? Enumerable.Empty<IValue>()).Distinct();
        ValueEventArgs = triggerContexts.SelectMany(tc => tc.ValueEventArgs ?? Enumerable.Empty<ValueEventArgs>()).Distinct();
        Timestamp = triggerContexts.Min(tc => tc.Timestamp);
    }
}

public static class ShutterAutomationComputationTriggerContextExtensions
{
    /// <summary>
    /// Assesses each trigger in the context's collection for it's firing time vs. urgency.
    /// If a trigger is already overdue for processing based on its urgency, the method returns TimeSpan.Zero to indicate that processing should start immediately.
    /// Otherwise, it returns the remaining time until the earliest trigger in the collection is due for processing, which can be used to delay the processing of the triggers to allow for more triggers to arrive
    /// and be processed together, e.g. to avoid excessive shutter movements in case of rapidly changing input conditions; if the returned TimeSpan is used for delaying the processing, the method should be called again after the delay to reassess the remaining time until processing, as new triggers might have arrived in the meantime or the urgency of existing triggers might have changed.
    /// </summary>
    /// <param name="currentTime"></param>
    /// <returns></returns>
    public static TimeSpan GetRemainingTimeUntilProcessing(this IEnumerable<ShutterAutomationComputationTriggerContext> triggerContexts, DateTimeOffset currentTime)
    {
        var remainingTimes = triggerContexts.Select(tc =>
        {
            var urgency = tc.Urgency;
            var maxWaitingTime = MaximumTriggerWaitingTimes[urgency];
            var timeSinceTriggering = currentTime - tc.Timestamp;
            if ( timeSinceTriggering >= maxWaitingTime)
                return TimeSpan.Zero; // trigger is already overdue for processing, so we should process immediately
            var remainingTime = maxWaitingTime - timeSinceTriggering;
            return remainingTime;
        });

        var minRemainingTime = remainingTimes.Min();
        return minRemainingTime > TimeSpan.Zero ? minRemainingTime : TimeSpan.Zero;
    }

    private static readonly Dictionary<ShutterAutomationComputationTriggerUrgency, TimeSpan> MaximumTriggerWaitingTimes = new()
    {
        { ShutterAutomationComputationTriggerUrgency.Slow, TimeSpan.FromSeconds(5) },
        { ShutterAutomationComputationTriggerUrgency.Normal, TimeSpan.FromSeconds(1) },
        { ShutterAutomationComputationTriggerUrgency.Immediate, TimeSpan.Zero }
    };
}