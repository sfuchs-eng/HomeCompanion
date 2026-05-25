# Architecture Specification: Alerting Extension for HomeCompanion

**Date:** 2026-05-25  
**Status:** Proposed  
**Owner:** HomeCompanion.Integrations.Alerting

## 1. Purpose

Define the target architecture and functional contract for HomeCompanion user alerting so logic modules can publish alerts through abstracted channels while transport details remain in extension providers.

This specification covers:
- Abstractions to be added in HomeCompanion.Abstractions (using HomeCompanion namespaces)
- Concrete extension design in HomeCompanion.Integrations.Alerting
- Alert delivery behavior for critical, warning, and user-info scenarios
- Named alert lifecycle behavior and user interaction via IValuesContainer

## 2. Scope

In scope:
- Fire-and-forget alerts (most common)
- Named alert state-machine alerts
- Severity-driven channel selection with configurable mapping
- Critical alert publication to MQTT (short message plus metadata JSON)
- Warning alert delivery by email to configured recipients, with MQTT fallback
- User-info email delivery to logic-specified recipients
- Parallel delivery over multiple alert paths
- Persistence of named alert states through existing state store infrastructure
- Reminder scheduling for active named alerts
- Extensibility model for future channels

Out of scope:
- Full UI implementation in HomeCompanion.Server
- Replacing existing MQTT integration stack
- Replacing persistence subsystem
- External ticketing/on-call integrations in v1

## 3. Context and Constraints

- HomeCompanion is event-driven; values are initialized and routed centrally by ValuesManager.
- Extension registration follows IExtensionRegistration and must not perform runtime work in registration itself.
- Connectivity and background work use hosted services.
- Time semantics use TimeProvider; direct wall-clock access APIs are not used in implementation.
- Tests must run offline.

## 4. Functional Requirements

## 4.1 Alert Classes

1. Critical alerts
- Publish short user-facing message to MQTT topic.
- MQTT broker and topic are configurable.
- Payload is JSON containing message text and alert metadata.

2. Warning alerts
- Send email to configurable recipient list.
- If email route is not available (for example not configured, temporarily unavailable, or delivery fails after retry policy), route warning through critical MQTT path as fallback.

3. User-info alerts
- Send email to recipient address(es) explicitly supplied by logic per alert call.
- If recipient is missing, reject request and log warning; no fallback route.

## 4.2 Alert Types

1. Fire-and-forget
- One-shot user message.
- No persistent status model required.
- Delivery result is tracked for diagnostics and optional caller response.

2. Named alert
- Alert identified by stable alert key.
- Lifecycle statuses:
  - Monitoring
  - Alert
  - Acknowledged
  - Disabled
- Service owns state machine transitions.
- Logic reports trigger and reset intents; logic does not set arbitrary status directly.
- State is persisted and restored on startup.
- Active Alert state supports periodic reminders (configurable interval).
- when a Logic triggers a yet unknown alert key, the service creates a new named alert instance with that key and starts managing its lifecycle.

## 4.3 Parallel Path Delivery

- Channel/path delivery may run in parallel.
- Delivery result is aggregated per alert request:
  - per-path success/failure details
  - overall success policy defined by severity and fallback behavior
- One failing path must not block execution of other paths.

## 5. Decision Summary (Locked)

1. Spec location: docs/architecture/homecompanion-alerting-arch-spec.md.
2. Channel selection model: provider chooses channels from configurable severity mapping.
3. Critical MQTT payload: JSON with short text plus alert key metadata.
4. Named alert persistence: yes, via existing state store.
5. Named alert controls via values: per-alert Ack command, Disabled switch, Status, LastChange.
6. Warning email behavior with missing/failed recipient route: fallback to critical MQTT route.
7. User-info with missing recipients: reject and log warning.
8. Named lifecycle ownership: alerting service owns state machine.
9. Repeat policy for active alert: periodic reminders with configurable interval.
10. Severity taxonomy: extensible enum in v1.

## 6. Proposed Abstractions (HomeCompanion.Abstractions)

Note: Namespace follows project/folder conventions and does not introduce a separate Abstractions namespace.

## 6.1 Core Contracts

Proposed interfaces (conceptual):

- IAlertingService
  - Logic-facing entry point for alert publication and named alert intents.
  - Async methods with CancellationToken.
  - Returns structured delivery/state result object.

- INamedAlertRegistry (optional internal abstraction)
  - State management, persistence integration, transition validation.
  - Can remain internal to extension if no cross-project requirement appears.

- IAlertChannelProvider
  - Provider-facing abstraction for a concrete route (MQTT, Email, future channels).
  - Sends an already-resolved outbound message.

## 6.2 Enums and Models

Proposed enums:

- AlertSeverity
  - Debug
  - Info
  - Warning
  - Critical
  - Emergency

- AlertPath (abstract route identity, extensible)
  - PushMessage (MQTT topic push bridge)
  - Email
  - Future values allowed (for example Sms, VoiceCall, Webhook)

- NamedAlertStatus
  - Monitoring
  - Alert
  - Acknowledged
  - Disabled

Proposed request/result models:

- AlertRequest
  - Severity
  - MessageShort
  - MessageLong optional
  - CorrelationId optional
  - Metadata dictionary optional
  - Recipient override optional (for user-info)

- NamedAlertIntent
  - AlertKey
  - Intent type: Trigger, Reset, Acknowledge, Enable, Disable
  - Current signal context metadata

- AlertDispatchResult
  - Overall status
  - Per-path outcomes
  - Retry/fallback trace

## 6.3 Behavior Semantics

- Logic calls are non-blocking relative to independent paths by using parallel dispatch.
- API exposes cancellation and timeout semantics.
- Caller receives deterministic result classification even under partial failures.
- Internal exceptions are contained and logged with structured context.

## 7. Concrete Extension: HomeCompanion.Integrations.Alerting

## 7.1 Main Components

- AlertingExtensionRegistration
  - Binds options.
  - Registers alerting services.
  - Registers providers and hosted background components.

- AlertingService
  - Implements IAlertingService.
  - Resolves route mapping from severity.
  - Executes dispatch fan-out and fallback policies.

- Providers
  - MqttAlertChannelProvider
  - EmailAlertChannelProvider

- Named alert services
  - NamedAlertStateMachine
  - NamedAlertPersistenceAdapter
  - ReminderScheduler

- Values container for user interaction
  - AlertingValues (or generated/grouped variant)
  - Exposes per-alert interaction values.

## 7.2 Dependencies

- Existing MQTT integration, keyed by configured broker name.
- Mail transport dependency: MailKit Light (as requested).
- Existing state store for persistence.
- TimeProvider for reminder timing and timestamps.
- Standard logging and diagnostics abstractions.

## 7.3 MQTT Critical Path

- Routing target selected by configuration:
  - broker name
  - topic
  - qos, retain, content type optional
- Payload format (JSON) example shape:

```json
{
  "severity": "Critical",
  "message": "Water leak detected in utility room",
  "alertKey": "WaterLeak.UtilityRoom",
  "timestamp": "2026-05-25T18:23:12.345+00:00",
  "correlationId": "e3f9cdb5-45d9-4fa8-84e3-a1ba2abf7b77",
  "metadata": {
    "sourceLogic": "WaterLeakProtectionLogic",
    "sensor": "LeakSensor_01"
  }
}
```

- Minimum guaranteed fields:
  - message
  - severity
  - timestamp
  - alertKey when available

## 7.4 Email Paths

Warning email path:
- Uses configured recipient array.
- Supports subject/body template config.
- On unavailable/failed email route, executes MQTT fallback route according to warning fallback policy.

User-info email path:
- Requires recipient(s) in request.
- Rejects missing-recipient requests.
- Supports optional template and metadata rendering.

## 7.5 Parallelism and Reliability

- Dispatch is task-parallel per resolved path.
- Path-level retry policy is configurable and isolated by provider.
- Aggregate completion waits only for selected route tasks and fallback tasks.
- Circuit-breaker and backoff are future-ready but optional for v1.

## 8. Named Alerts Domain Model

## 8.1 Lifecycle and Transitions

Default steady state: Monitoring.

Transitions:

- Monitoring -> Alert
  - Trigger intent received and not Disabled.

- Alert -> Acknowledged
  - User acknowledgment intent/value received.

- Alert -> Monitoring
  - Reset intent received and condition cleared.

- Acknowledged -> Monitoring
  - Reset intent received and condition cleared.

- Any -> Disabled
  - User disable command received.

- Disabled -> Monitoring
  - User enable command received.

Guard rules:
- Trigger intent while Disabled does not move to Alert.
- Repeated Trigger while already Alert does not duplicate transition event, but may refresh reminder baseline according to configuration.
- Illegal transitions are ignored and logged at debug/warning level with context.

## 8.2 Reminder Behavior

- Applies to Alert status only.
- Interval is configurable per severity or per alert key override.
- Reminder task uses TimeProvider-based scheduler.
- Reminder dispatch is suppressed when status is Acknowledged, Monitoring, or Disabled.

## 8.3 De-duplication and Idempotency

- Named alert key plus transition type plus coarse time window can be used for deduplication.
- CorrelationId supports cross-system traceability.
- Optional event sequence number can be added later.

## 9. IValuesContainer Contract for Named Alert User Interaction

Each named alert exposes value endpoints for user interaction and visibility.

Minimum per-alert values:
- Ack command (write-able bool pulse or command semantic)
- Disabled switch (bool)
- Status (string or enum-backed value)
- LastChange timestamp (DateTimeOffset)

Optional values:
- LastMessage
- ReminderDueAt
- TransitionCount

Responsibilities:
- Logic interacts with IAlertingService, not directly with status values for lifecycle control.
- Value writes from users/UI are interpreted by alerting service as intents.
- Values are persisted/restored according to existing state store behavior.

## 10. Configuration Contract (Draft)

```json
{
  "Alerting": {
    "Enable": true,
    "SeverityRouting": {
      "Debug": [],
      "Info": ["Email"],
      "Warning": ["Email"],
      "Critical": ["MqttPushBridge"],
      "Emergency": ["MqttPushBridge", "Email"]
    },
    "Fallbacks": {
      "WarningEmailToCriticalMqtt": true
    },
    "Mqtt": {
      "Critical": {
        "Broker": "main",
        "Topic": "homecompanion/alerts/critical",
        "Qos": 1,
        "Retain": false,
        "ContentType": "application/json"
      }
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
        "InfoSubject": "[HomeCompanion] Info"
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

Validation rules:
- If Alerting disabled, service remains no-op except diagnostics.
- If warning routing includes Email and recipient list is empty, warning route attempts fallback to critical MQTT.
- If critical MQTT route is selected but broker/topic is invalid, dispatch fails with diagnostics and no implicit alternate fallback unless configured.
- If user-info request lacks recipients, request is rejected.

## 11. Error Handling and Observability

Logging:
- Structured logs with alert key, severity, route, provider, correlation id.
- Warning-level logs for route failures.
- Debug-level logs for ignored transitions and dedup decisions.

Diagnostics:
- Counters:
  - alerts_requested_total
  - alerts_dispatched_total
  - alerts_failed_total
  - alerts_fallback_total
  - named_alert_transitions_total
  - named_alert_reminders_total
- Optional health snapshot via IDiagnostic implementation.

Security:
- Redact secrets and sensitive payload sections in logs.
- SMTP credential fields must not be emitted to logs.

## 12. Extensibility Model

- New routes are added by implementing IAlertChannelProvider and extending AlertPath mapping.
- Severity taxonomy is extensible by enum evolution; route mapping and template selection are config-driven.
- Named alert behaviors support per-alert overrides without changing logic API.

Potential future providers:
- SMS
- Voice call
- Push gateway APIs
- Webhooks

## 13. Additional Home-Automation Dimensions

Recommended dimensions beyond v1 core:

1. Quiet hours and escalation windows
- Delay non-critical notifications overnight.
- Escalate unresolved emergency alerts after configurable duration.

2. Maintenance and suppression windows
- Suppress selected alert keys during maintenance.
- Record suppressed events for audit.

3. Flapping protection
- Debounce and hysteresis thresholds for noisy sensors.
- Minimum stable duration before transition to Alert.

4. Presence-aware routing
- Route to different recipient sets based on occupancy mode (home/away/vacation).

5. Localization and templates
- Language-specific template rendering.
- Device-context formatting for OpenHAB-friendly push messages.

6. Offline/backlog behavior
- Policy options: drop, compact, or spool pending alerts per channel.
- Ensure bounded memory and clear diagnostic reporting.

## 14. Testing and Verification

## 14.1 Unit Tests

- Severity routing resolution to paths.
- Warning email failure/missing configuration fallback to critical MQTT.
- User-info missing recipients rejection.
- Named alert transition validation and guard logic.
- Reminder scheduling and suppression by status.
- Dedup behavior and correlation propagation.

## 14.2 Integration Tests (Offline)

- MQTT provider interaction with fake or test broker abstraction.
- Email provider interaction with MailKit Light test doubles.
- Persistence restore and transition continuation after simulated restart.
- Parallel path dispatch with partial failures.

## 14.3 Failure Mode Tests

- Invalid configuration handling.
- Provider timeout/cancellation handling.
- Persistent store unavailable on startup.
- Repeated transient provider failures with retries and fallbacks.

## 14.4 Acceptance Checklist

1. Critical alert from logic appears on configured MQTT topic with required JSON fields.
2. Warning alert sends email to configured recipients.
3. Warning alert falls back to MQTT when email route is unavailable.
4. User-info alert sends only when logic supplies recipient(s).
5. Named alert transitions correctly through Monitoring/Alert/Acknowledged/Disabled.
6. Named alert status and control values are visible and writable through values infrastructure.
7. Reminder notifications occur for active Alert state according to configuration.
8. State is restored correctly after restart.

## 15. Implementation Guidance (Non-Normative)

Suggested project/file layout:

- HomeCompanion.Integrations.Alerting/
  - AlertingIntegrationOptions.cs
  - AlertingExtensionRegistration.cs
  - AlertingService.cs
  - Providers/
    - IAlertChannelProvider.cs
    - MqttAlertChannelProvider.cs
    - EmailAlertChannelProvider.cs
  - Named/
    - NamedAlertStateMachine.cs
    - NamedAlertPersistenceAdapter.cs
    - ReminderScheduler.cs
  - Values/
    - AlertingValues.cs

- HomeCompanion.Abstractions/
  - Alerting/
    - IAlertingService.cs
    - AlertSeverity.cs
    - AlertPath.cs
    - NamedAlertStatus.cs
    - AlertRequest.cs
    - AlertDispatchResult.cs

## 16. Open Questions (Post-v1)

- Should reminder intervals support cron-style schedules?
- Should user acknowledgments be actor-attributed and audited?
- Should emergency alerts enforce minimum dual-route delivery before success classification?

## 17. Change Log

- 2026-05-25: Initial specification drafted from high-level requirements and design decisions.