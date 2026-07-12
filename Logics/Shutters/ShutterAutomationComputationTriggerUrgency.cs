namespace HomeCompanion.Logics.Shutters;

public enum ShutterAutomationComputationTriggerUrgency
{
    Undefined = 0,

    /// <summary>
    /// Slow changing environmental conditions, e.g. sun position, weather conditions, etc. which can be processed with a delay of several seconds to minutes.
    /// Wait for seconds...minute before processing, awaiting potentially more triggers
    /// </summary>
    Slow = 1,

    /// <summary>
    /// Wait ~10 seconds before processing, awaiting potentially more triggers
    /// </summary>
    Normal = 2,

    /// <summary>
    /// The trigger should be processed within less than a second.
    /// E.g. user requests, safety overrides, ...
    /// </summary>
    Immediate = 3
}
