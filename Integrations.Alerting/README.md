# HomeCompanion Alerting Integration

HomeCompanion.Integrations.Alerting provides user alerting for HomeCompanion logic modules.

It implements:

- fire-and-forget alert dispatch
- named alert lifecycle tracking
- push-message delivery through MQTT
- e-mail delivery through SMTP (MailKit)
- severity-to-path routing with configurable fallback behavior
- named-alert persistence through the existing state store and state initialization pipeline

## Main components

- AlertingService: logic-facing service implementing IAlertingService
- PushMessageAlertChannelProvider: publishes alert JSON to MQTT topics using keyed IMqttBrokerConnection
- EmailAlertChannelProvider: sends e-mails using MailKit SMTP client
- NamedAlertStateMachine: applies named-alert intents and manages transitions
- AlertingValues: dynamic values container exposing per-alert acknowledge/disable/status/last-change values
- NamedAlertPersistenceAdapter: loads/saves named-alert state snapshots

## Configuration

Configuration root is Alerting.

```json
{
  "Alerting": {
    "Enable": true,
    "SeverityRouting": {
      "Info": ["Email"],
      "Warning": ["Email"],
      "Critical": ["PushMessage"],
      "Emergency": ["PushMessage", "Email"]
    },
    "Fallbacks": {
      "WarningEmailToCriticalPushMessage": true
    },
    "PushMessage": {
      "Broker": "main",
      "Topic": "homecompanion/alerts/critical",
      "Qos": 1,
      "Retain": false,
      "ContentType": "application/json",
      "PublishResultTimeoutMs": 5000
    },
    "Email": {
      "WarningRecipients": [
        "ops@example.org",
        "home@example.org"
      ],
      "Smtp": {
        "Host": "smtp.example.org",
        "Port": 587,
        "UseStartTls": true,
        "User": "alerts@example.org",
        "Password": "***",
        "From": "alerts@example.org"
      },
      "Templates": {
        "WarningSubject": "[HomeCompanion] Warning: {AlertKey}",
        "InfoSubject": "[HomeCompanion] Info",
        "CriticalSubject": "[HomeCompanion] Critical: {AlertKey}",
        "Body": "Severity: {Severity}\\nAlertKey: {AlertKey}\\nMessage: {MessageShort}\\nDetails: {MessageLong}\\nCorrelationId: {CorrelationId}\\nMetadata:\\n{Metadata}"
      }
    },
    "NamedAlerts": {
      "PersistState": true,
      "DefaultReminderInterval": "00:15:00",
      "PerSeverityReminderInterval": {
        "Warning": "00:30:00",
        "Critical": "00:10:00",
        "Emergency": "00:05:00"
      }
    }
  }
}
```

## Routing behavior

- AlertService resolves paths from SeverityRouting.
- Selected paths are dispatched in parallel.
- Warning alerts can fall back from Email to PushMessage when WarningEmailToCriticalPushMessage is enabled and e-mail path fails.
- User-info style messages require explicit recipients in the alert request.

## Push-message provider

PushMessageAlertChannelProvider:

- resolves the configured broker via keyed IMqttBrokerConnection
- publishes JSON payload to PushMessage.Topic
- applies optional Qos, Retain, and ContentType settings
- waits for publish callback result up to PublishResultTimeoutMs

Payload fields:

- severity
- message
- messageLong
- alertKey
- timestamp
- correlationId
- metadata

## E-mail provider

EmailAlertChannelProvider:

- resolves recipients from request override, or WarningRecipients for warning alerts
- connects via SMTP host/port using StartTLS or auto socket mode
- authenticates when SMTP user is configured
- renders subject/body templates and sends plain-text messages

Template placeholders:

- {Severity}
- {AlertKey}
- {MessageShort}
- {MessageLong}
- {CorrelationId}
- {Metadata}

## Named-alert lifecycle

NamedAlertStateMachine statuses:

- Monitoring
- Alert
- Acknowledged
- Disabled

Intent handling:

- Trigger: transitions to Alert unless currently Disabled
- Acknowledge: transitions Alert to Acknowledged
- Reset: transitions Alert or Acknowledged to Monitoring
- Disable: transitions any state to Disabled
- Enable: transitions Disabled to Monitoring

When a logic triggers an unknown alert key, a new named alert entry is created and managed.

## Values integration

AlertingValues creates and exposes per-alert dynamic values:

- Acknowledge (write bool)
- Disabled (write bool)
- Status (string)
- LastChange (DateTimeOffset)

Writes to Acknowledge and Disabled are converted into named-alert intents and applied to the state machine.

## Persistence integration

Named alert states are persisted in state set named-alerts.

- load hook: InitLoadFromStore stage
- save hook: shutdown save stage

Integration is registered via NamedAlertStateInitializationHostedService.

## Testing

Focused tests are located in [Tests/AlertingNamedAlertsTests.cs](Tests/AlertingNamedAlertsTests.cs):

- named-alert transition behavior
- AlertingValues write-to-intent behavior

## Related docs

- Architecture spec: [docs/architecture/homecompanion-alerting-arch-spec.md](docs/architecture/homecompanion-alerting-arch-spec.md)
- Main project docs: [README.md](README.md)
- MQTT integration docs: [Integrations.Mqtt/README.md](Integrations.Mqtt/README.md)
