namespace HomeCompanion.Abstractions;

[Flags]
public enum ValueStatus
{
    /// <summary>
    /// The value is in its default state, e.g. after system start before any initialization or update.
    /// </summary>
    Default = 0,

    /// <summary>
    /// The value is loaded with a value persisted prior last shutdown.
    /// If the shutdown period was short, the value might be sufficiently up to date.
    /// </summary>
    Loaded = 1 << 0,

    /// <summary>
    /// The value is initialized with a value read from the bus or an API call.
    /// It can be assumed to be the current value of the bus data point or API data point unless further updates were missed due to e.g. communication errors.
    /// </summary>
    Initialized = 1 << 1,

    /// <summary>
    /// Some values may have a valid range of values. If the value is outside of this range, it is marked as out of range.
    /// </summary>
    OutOfRange = 1 << 2,

    /// <summary>
    /// The value is in an error state, e.g. due to communication errors or invalid data received.
    /// In this state, the value should not be used for any logic processing and should be updated as soon as possible to get back to a valid state.
    /// Initialization incl. received writes/read-answers/updates would reset the error state.
    /// </summary>
    Error = 1 << 3
}
