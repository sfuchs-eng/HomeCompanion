using System.Threading.Channels;
using HomeCompanion.Base.Model;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

public partial class ShutterController
{
    /// <summary>
    /// The <b>shutter automation computation loop</b> processes the collected triggers and determines the desired target state for each shutter, e.g. based on time of day, weather conditions, user preferences, etc., and enqueues the resulting shutter targets into the <b>shutter target processing loop</b>.
    /// There's no need for batching/debouncinng. Just process one trigger at a time and update the target state for the affected shutter(s) accordingly, as each trigger is expected to potentially change the target state of one or more shutters, and we want to react to changes as quickly as possible, e.g. when a trigger is indicating that a shutter was manually overridden via a wall switch or remote control, we want to immediately update the target state for that shutter in order to pause automation for it and avoid unwanted automatic movements.
    /// </summary>
    /// <param name="channel"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    private async Task ProcessShutterAutomationComputationAsync(Channel<ShutterAutomationComputationTriggerContext> channel, CancellationToken token)
    {
        while (await channel.Reader.WaitToReadAsync(token))
        {
            while (channel.Reader.TryRead(out var triggerContext))
            {
                try
                {
                    var shutterKeys = DetermineAffectedShutters(triggerContext);
                    logger.LogTrace("Processing shutter automation computation trigger for shutter(s) {ShutterKeys} (batch of {BatchSize})", string.Join(", ", shutterKeys.Select(k => k.Key)), shutterKeys.Count());
                    foreach (var shutterKey in shutterKeys)
                    {
                        try
                        {
                            logger.LogTrace("Processing shutter automation computation trigger for shutter {ShutterKey}", shutterKey.Key);
                            var buildingRuntime = buildingRuntimes.GetValueOrDefault(shutterKey.RoomKey.BuildingKey) ?? throw new InvalidOperationException($"No runtime found for building {shutterKey.RoomKey.BuildingKey.Key} affected by automation computation trigger for shutter {shutterKey.Key}");
                            var roomRuntime = roomRuntimes.GetValueOrDefault(shutterKey.RoomKey) ?? throw new InvalidOperationException($"No runtime found for room {shutterKey.RoomKey.Key} affected by automation computation trigger for shutter {shutterKey.Key}");
                            var shutterRuntime = shutterRuntimes.GetValueOrDefault(shutterKey) ?? throw new InvalidOperationException($"No runtime found for shutter {shutterKey.Key} affected by automation computation trigger");
                            var runtimeContext = new RuntimeContext(shutterKey, buildingRuntime, roomRuntime, shutterRuntime);
                            await ComputeShutterTargetStateAsync(runtimeContext, triggerContext, token);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Error processing shutter automation computation trigger for shutter {ShutterKey}: {Message}",
                                shutterKey.Key, ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error processing shutter automation computation trigger for shutter {ShutterKey} (batch of {BatchSize}): {Message}",
                        triggerContext.ThingKeys.First().Key, triggerContext.ThingKeys.Count(), ex.Message);
                }
            }
        }
    }

    private IEnumerable<ShutterKey> DetermineAffectedShutters(ShutterAutomationComputationTriggerContext triggerContext)
    {
        // determine which shutter(s) are affected by the given trigger context, e.g. if the trigger is related to a specific shutter input, then the affected shutter is the one associated with that input, but if the trigger is related to a time-based criterion, then the affected shutters might be all shutters that have a time-based criterion depending on the current time, and similarly for weather-based criteria, user preference criteria, etc.
        switch ( triggerContext.Scope )
        {
            case ShutterAutomationComputationScope.ShutterSpecific:
                return triggerContext.ThingKeys;
            case ShutterAutomationComputationScope.Global:
            case ShutterAutomationComputationScope.Undefined:
            default:
                return shutterRuntimes.Keys; // if the scope is global or undefined, we conservatively assume that all shutters could potentially be affected and return all shutter keys, which ensures that we don't miss any updates but might result in some unnecessary computations for unaffected shutters, but that's an acceptable trade-off for simplicity and correctness
        }
    }

    private record RuntimeContext(ShutterKey ShutterKey, BuildingRuntime? BuildingRuntime, RoomRuntime? RoomRuntime, ShutterRuntime? ShutterRuntime);

    private async Task ComputeShutterTargetStateAsync(RuntimeContext runtimeContext, ShutterAutomationComputationTriggerContext triggerContext, CancellationToken token)
    {
        // 1. compute individual criteria results for the affected shutter(s) based on the trigger context, e.g. evaluate time-based criteria, weather-based criteria, user preference criteria, etc., and determine which criteria are currently met for each affected shutter
        // 2. prioritize the criteria results based on their implied priority and derive the desired target state for the affected shutter based on the prioritized criteria results, e.g. if a time-based criterion is met that indicates that the shutter should be closed, but at the same time a user preference criterion is met that indicates that the shutter should be open, then we need to determine which criterion takes precedence based on their implied priority and set the target state accordingly, e.g. if we decide that user preferences take precedence over time-based criteria, then we would set the target state to open in this case
        // 3. File the shutter target state update into the shutter target processing loop by enqueuing a new ShutterTargetStateUpdateContext containing the affected shutter(s) and their new target state, which will then be processed by the shutter target processing loop to actually update the target state of the affected shutter(s) in the system, e.g. by writing to their position or open/close inputs, and also to update any relevant internal state in the runtimes, e.g. to track whether a shutter is currently overridden or not, etc.
        throw new NotImplementedException();
    }
}
