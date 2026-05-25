namespace HomeCompanion.Alerting;

/// <summary>
/// Abstract delivery paths that alerting providers can implement.
/// </summary>
public enum AlertPath
{
    /// <summary>
    /// A push-style message path, typically backed by MQTT and then bridged to mobile push services.
    /// </summary>
    PushMessage,

    /// <summary>
    /// Electronic mail delivery.
    /// </summary>
    Email,
}
