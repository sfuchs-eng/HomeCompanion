using Microsoft.Extensions.Hosting;

namespace HomeCompanion;

/// <summary>
/// Implemented by components that bridge an external type of bus or API (e.g. KNX, OpenHAB, MQTT) to the HomeCompanion event system.
/// The component is used as a singleton service and needs to handle all buses of that type (e.g. all KNX interfaces) if there are multiple.
/// <br/>
/// Connectivity providers are the entry points for system integrations. Typically they are in separate libraries within the HomeCompanion.Integrations namespace,
/// and are responsible for translating between the external system's message format and the internal <see cref="IEvent"/> format, as well as for managing the connection lifecycle and reliability concerns of the external system.
/// <br/>
/// <see cref="IConnectivityProvider"/>s are registered as <see cref="IConnectivityProvider"/> and as <see cref="IHostedService"/> automatically by the host, so they will be started and stopped with the application and can run background loops to maintain connections and process messages in real time.
/// </summary>
/// <remarks>
/// A connectivity provider is responsible for translating inbound bus messages into <see cref="IEvent"/> instances
/// published via <see cref="IEventPublisher"/>, and for forwarding outbound events to the external system by
/// subscribing via <see cref="IEventSubscriber"/>.<br/>
/// The connection lifecycle is managed by the host via the inherited <see cref="IHostedService"/> contract.
/// A connectivity provider will run a background loop that maintains the connection and processes messages in real time.
/// It would typically handle reconnection logic, message batching, queueing/retrying, and other concerns specific to the external system.
/// </remarks>
public interface IConnectivityProvider : IHostedService
{
    /// <summary>
    /// Are we connected to the bus(es)? Can messages be sent/received?
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Has the initialization of values and subscriptions completed to the extent possible?
    /// E.g. have readable KNX group addresses been read and corresponding datapoint initialization events launched?
    /// </summary>
    bool IsInitializationFinished { get; }
}
