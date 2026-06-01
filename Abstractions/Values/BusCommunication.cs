using System.Text.Json.Serialization;
using HomeCompanion.Abstractions.Serialization;

namespace HomeCompanion.Values;

/// <summary>
/// Specifies the communication a bus mapping allows between an <see cref="IValue"/> and a bus address/datapoint (e.g. KNX group address).
/// TODO: presently kept simple. Consider extending towards what KNX allows for device objects (see comment in source file of this enum) if needed in the future.
/// Could also be entirely replaced by the KNX-specific <see cref="KnxObjectBusCommunication"/> if the communication requirements of other bus mappings are similar enough or can be expressed via the same flags. For now, we keep it separate to avoid unnecessary coupling of HomeCompanion core abstractions to KNX-specific concepts.
/// </summary>
[JsonConverter(typeof(CommaSeparatedFlagsEnumJsonConverter<BusCommunication>))]
[Flags]
public enum BusCommunication
{
    None = 0,

    /// <summary>
    /// Receive messages/events from the correpsonding bus and update mapped <see cref="IValue"/> instances accordingly.
    /// </summary>
    Receive = 1,

    /// <summary>
    /// Transmit value changes of mapped <see cref="IValue"/> instances to the bus.
    /// </summary>
    Transmit = 2,

    /// <summary>
    /// Answer read requests from the bus with the mapped <see cref="IValue"/>'s current value.
    /// </summary>
    AnswerReadRequests = 4,

    /// <summary>
    /// If set, the mapping is only used for initial state retrieval during value initialization, not for ongoing receive/transmit operations. E.g. use OpenHAB for KNX group address value initialization.
    /// Means it may receive and transmit but, unless combined with other flags, must only do so in context of value initialization, not during regular operation.
    /// E.g. used to initalize KNX Group Address mapped values from OpenHAB items.
    /// </summary>
    /// <typeparam name="TBus"></typeparam>
    /// <typeparam name="TAddress"></typeparam>
    Initialize = 1 << 30,

    //-------------------------------

    RegularCommunication = Receive | Transmit
}

/* KNX acc. Gemini:
Flag	Name	        Function
C	    Communication   The "Master Switch." If this is not set, the object cannot communicate with the bus at all. It must be active for any other flags to function.
R	    Read	        Allows other devices on the bus to request the current value of this object via a GroupValue_Read telegram. The device will respond with a GroupValue_Response.
W	    Write	        Allows the object’s value to be changed by other devices via a GroupValue_Write telegram. This is typical for actuators (e.g., a relay receiving an "ON" command).
T	    Transmit	    Allows the device to spontaneously send its value to the bus (e.g., a sensor sending a temperature update or a switch sending a toggle command).
U	    Update	        If set, the object will update its internal value when it sees a GroupValue_Response on the bus for its group address, even if it didn't request the data.
I	    Initialize	    (Less common) Forces the device to send a Read Request upon power-up to synchronize its state with the rest of the installation.
*/