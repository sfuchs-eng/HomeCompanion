using HomeCompanion.Alerting;

namespace HomeCompanion.Integrations.Alerting;

/// <summary>
/// Root options for alerting integration.
/// </summary>
public sealed class AlertingIntegrationOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Alerting";

    /// <summary>
    /// Enables or disables alerting behavior.
    /// </summary>
    public bool Enable { get; init; } = true;

    /// <summary>
    /// Severity-to-path routing configuration.
    /// </summary>
    public Dictionary<AlertSeverity, List<AlertPath>> SeverityRouting { get; init; } = [];

    /// <summary>
    /// Fallback options.
    /// </summary>
    public AlertingFallbackOptions Fallbacks { get; init; } = new();

    /// <summary>
    /// Push-message route options.
    /// </summary>
    public PushMessageAlertingOptions PushMessage { get; init; } = new();

    /// <summary>
    /// E-mail route options.
    /// </summary>
    public EmailAlertingOptions Email { get; init; } = new();

    /// <summary>
    /// Named-alert options.
    /// </summary>
    public NamedAlertingOptions NamedAlerts { get; init; } = new();

    /// <summary>
    /// Resolves configured paths for a severity.
    /// </summary>
    /// <param name="severity">Alert severity.</param>
    /// <returns>Configured path list, or empty list when not configured.</returns>
    public IReadOnlyList<AlertPath> GetPaths(AlertSeverity severity)
        => SeverityRouting.TryGetValue(severity, out var paths)
            ? paths
            : [];
}

/// <summary>
/// Alerting fallback options.
/// </summary>
public sealed class AlertingFallbackOptions
{
    /// <summary>
    /// Enables warning e-mail fallback to push-message path.
    /// </summary>
    public bool WarningEmailToCriticalPushMessage { get; init; } = true;
}

/// <summary>
/// Push-message route options.
/// </summary>
public sealed class PushMessageAlertingOptions
{
    /// <summary>
    /// Broker name used by push-message provider.
    /// </summary>
    public string Broker { get; init; } = string.Empty;

    /// <summary>
    /// Topic for critical push messages.
    /// </summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary>
    /// Optional MQTT QoS level.
    /// </summary>
    public int? Qos { get; init; }

    /// <summary>
    /// Optional retain setting.
    /// </summary>
    public bool? Retain { get; init; }

    /// <summary>
    /// Optional content type.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Wait timeout in milliseconds for publish completion callback.
    /// </summary>
    public int PublishResultTimeoutMs { get; init; } = 5000;
}

/// <summary>
/// E-mail route options.
/// </summary>
public sealed class EmailAlertingOptions
{
    /// <summary>
    /// Recipients for warning alerts.
    /// </summary>
    public List<string> WarningRecipients { get; init; } = [];

    /// <summary>
    /// SMTP transport options.
    /// </summary>
    public EmailSmtpOptions Smtp { get; init; } = new();

    /// <summary>
    /// Subject/body templates for outgoing e-mail messages.
    /// </summary>
    public EmailTemplateOptions Templates { get; init; } = new();
}

/// <summary>
/// E-mail template options.
/// </summary>
public sealed class EmailTemplateOptions
{
    /// <summary>
    /// Subject template for warning alerts.
    /// </summary>
    public string WarningSubject { get; init; } = "[HomeCompanion] Warning: {AlertKey}";

    /// <summary>
    /// Subject template for informational alerts.
    /// </summary>
    public string InfoSubject { get; init; } = "[HomeCompanion] Info";

    /// <summary>
    /// Subject template used for critical/emergency alerts routed to e-mail.
    /// </summary>
    public string CriticalSubject { get; init; } = "[HomeCompanion] Critical: {AlertKey}";

    /// <summary>
    /// Body template used for all e-mail messages.
    /// </summary>
    public string Body { get; init; } =
        "Severity: {Severity}\nAlertKey: {AlertKey}\nMessage: {MessageShort}\nDetails: {MessageLong}\nCorrelationId: {CorrelationId}\nMetadata:\n{Metadata}";
}

/// <summary>
/// SMTP transport options.
/// </summary>
public sealed class EmailSmtpOptions
{
    /// <summary>
    /// SMTP host.
    /// </summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>
    /// SMTP port.
    /// </summary>
    public int Port { get; init; } = 587;

    /// <summary>
    /// Enables STARTTLS.
    /// </summary>
    public bool UseStartTls { get; init; } = true;

    /// <summary>
    /// SMTP user name.
    /// </summary>
    public string User { get; init; } = string.Empty;

    /// <summary>
    /// SMTP password.
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Sender address.
    /// </summary>
    public string From { get; init; } = string.Empty;
}

/// <summary>
/// Named-alert behavior options.
/// </summary>
public sealed class NamedAlertingOptions
{
    /// <summary>
    /// Enables named-alert state persistence.
    /// </summary>
    public bool PersistState { get; init; } = true;

    /// <summary>
    /// Default reminder interval.
    /// </summary>
    public TimeSpan DefaultReminderInterval { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Optional per-severity reminder intervals.
    /// </summary>
    public Dictionary<AlertSeverity, TimeSpan> PerSeverityReminderInterval { get; init; } = [];
}
