namespace HomeCompanion.Values;

/// <summary>
/// Specifies the communication a bus mapping allows between an <see cref="IValue"/> and a bus address/datapoint (e.g. KNX group address).
/// TODO: presently kept simple. Consider extending towards what KNX allows for device objects (e.g. transmit, write, response to read, update, communication mode, etc.) if needed in the future.
/// </summary>
[Flags]
public enum BusCommunication
{
    None = 0,

    Receive = 1,

    Transmit = 2,

    /// <summary>
    /// If set, the mapping is only used for initial state retrieval during value initialization, not for ongoing receive/transmit operations. E.g. use OpenHAB for KNX group address value initialization.
    /// </summary>
    /// <typeparam name="TBus"></typeparam>
    /// <typeparam name="TAddress"></typeparam>
    InitializeOnly = 1 << 30,

    //-------------------------------

    Full = Receive | Transmit
}
