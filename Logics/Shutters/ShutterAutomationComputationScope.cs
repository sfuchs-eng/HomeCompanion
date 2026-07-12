namespace HomeCompanion.Logics.Shutters;

public enum ShutterAutomationComputationScope
{
    Undefined = 0,

    /// <summary>
    /// The shutter automation computation should be performed for a specific shutter only.
    /// In case of e.g. room specific changes, just make it ShutterSpecific and add all relevant shutters to the trigger context.
    /// </summary>
    ShutterSpecific = 1,

    /// <summary>
    /// The shutter automation computation should be performed for all shutters in a specific room.
    /// </summary>
    RoomSpecific = 2,

    /// <summary>
    /// The shutter automation computation should be performed for all shutters in the system.
    /// </summary>
    Global = 3,
}
