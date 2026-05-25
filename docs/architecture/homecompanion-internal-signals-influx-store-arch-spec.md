# Architecture Specification: Internal Signals Store Using InfluxDB OSS v2

**Date:** 2026-05-25
**Status:** Proposed
**Owner:** HomeCompanion.Integrations.Influx (+ HomeCompanion.Abstractions contracts)

## 1. Purpose

Define a normative architecture for a service that allows `ILogic` implementations and other internal consumers to persist time-series internal events and signals in InfluxDB OSS v2 with buffered bulk writes.

The architecture is intentionally backend-swappable: stable contracts live in Abstractions, while concrete database implementations are packaged as extension projects.

This specification defines:
- The API and model split between Abstractions and Base
- Runtime implementation requirements
- Queueing and flush behavior
- Shutdown guarantees
- Configuration and dependency requirements

## 2. Normative Language

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, and MAY are to be interpreted as described in RFC 2119.

## 3. Scope

In scope:
- Write-path storage of internal events/signals to InfluxDB OSS v2
- Buffered queue with bulk flush triggers (time, queue size, shutdown)
- Single Influx endpoint/org/token per application instance
- Bucket routing with default bucket fallback and per-measurement override
- Service availability for `ILogic` implementations through DI

Out of scope:
- InfluxDB query/read APIs
- Multi-endpoint or multi-organization Influx topologies in one app instance
- InfluxDB v1 and InfluxDB v3 client/protocol support
- Durable on-disk buffering in first iteration

## 4. Architectural Context

- `ILogic` defines async initialization and enable/disable lifecycle and is consumed as a singleton service.
- `LogicBase` provides common logic conveniences but not transport/persistence I/O.
- Hosted services with channels must complete writers and handle cancellation gracefully on shutdown.

This store is an internal persistence utility and SHALL NOT replace the event bus or value lifecycle managers.

## 5. Decision Summary

1. A new abstraction for internal signal persistence SHALL be introduced in Abstractions.
2. Base SHALL provide optional logic ergonomics only, not runtime transport behavior.
3. Concrete Influx writing, queueing, batching, retries, and shutdown flush SHALL live in a dedicated extension project (`Integrations.Influx`), not in Core.
4. The implementation SHALL use the official InfluxDB OSS v2 .NET client package `InfluxDB.Client`.
5. Queue flush SHALL occur on any of:
- flush interval elapsed (default 10 seconds, configurable)
- max buffered point count reached (default 500, configurable)
- application shutdown
6. Exactly one Influx URL, organization, and token SHALL be configured per application instance.
7. A default bucket SHALL be required; per-measurement bucket override SHALL be supported.

## 6. Module Boundary Specification

## 6.1 Abstractions (REQUIRED)

Abstractions SHALL contain:
- A storage service interface for enqueueing internal signal measurements
- Transport-neutral payload models for measurements
- Optional options contract only if needed by consumers

Abstractions MUST NOT contain:
- References to `InfluxDB.Client`
- Influx-specific types (for example `PointData`) in public contracts
- Queueing/threading/network implementation details

### 6.1.1 Required Abstraction Contract Shape

The abstraction API SHALL support:
- Async enqueue of single measurement
- Async enqueue of multiple measurements
- Optional bucket override per measurement
- CancellationToken on async methods
- Tags and fields as flexible key/value pairs

The abstraction API SHOULD be non-blocking and suitable for hot paths used by logic modules.

### 6.1.2 Required Measurement Model Shape

A measurement model SHALL include at minimum:
- Measurement name
- Timestamp (`DateTimeOffset`)
- Tags (string key/value pairs)
- Fields (typed scalar values)
- Optional bucket override

## 6.2 Base (REQUIRED)

Base MAY contain:
- Convenience extension methods or helpers for logic authors to build measurement payloads
- Optional helper abstractions for reduced boilerplate in logic modules

Base MUST NOT contain:
- Influx client creation/usage
- Queue and batch flush workers
- Hosted service shutdown orchestration
- Retry/circuit/network policies

## 6.3 Dedicated Influx Extension Project (REQUIRED)

The Influx implementation SHALL be packaged in a dedicated extension project (recommended name: `HomeCompanion.Integrations.Influx`).

The Influx extension project SHALL contain:
- Concrete implementation of abstraction service
- InfluxDB client lifecycle management
- Buffered queue/channel and background flusher
- Shutdown flush logic
- DI registration via `IExtensionRegistration`
- Configuration binding and validation

The Influx extension project MUST NOT define the public cross-project persistence contract used by logic modules; those contracts belong to Abstractions.

## 6.4 Core Responsibilities (REQUIRED)

Core SHALL remain database-agnostic and SHALL NOT take a hard compile-time dependency on `InfluxDB.Client`.

Core MAY provide either:
- no default implementation (extension required), or
- a minimal no-op/fail-fast implementation bound to the abstraction to provide deterministic startup behavior when no persistence extension is installed.

If a default implementation is provided by Core, it MUST be clearly documented and MUST NOT perform hidden network/database writes.

## 7. InfluxDB OSS v2 Requirements

1. The implementation MUST use InfluxDB OSS v2 compatible API and behavior.
2. The implementation MUST depend on package `InfluxDB.Client`.
3. The implementation MUST NOT depend on Influx v1 client libraries or v3-specific client paths.
4. The implementation MUST initialize and reuse a singleton Influx client instance for normal operation.
5. The implementation MUST NOT create one client instance per write/flush.

## 8. Queueing and Flush Semantics

## 8.1 Queue Model

1. The service SHALL maintain an in-memory queue/channel of pending measurements.
2. Enqueue operations SHALL add measurements to this queue and return quickly.
3. The queue MAY be unbounded in first iteration, but memory behavior SHOULD be observable through metrics/logging.

## 8.2 Flush Triggers (OR Semantics)

A flush MUST be triggered when any one condition is true:
1. `FlushInterval` elapsed since last successful or attempted flush
- Default: 10 seconds
- Configurable
2. Buffered count reaches `MaxQueueSize`
- Default: 500 measurements
- Configurable
3. Shutdown begins
- Remaining queued measurements MUST be flushed before service stop completes (subject to host cancellation timeout)

## 8.3 Flush Behavior

1. Flush logic MUST group queued measurements by target bucket.
2. Bucket selection MUST follow:
- measurement-specific bucket override when present
- otherwise configured default bucket
3. Writes SHOULD be performed in bulk per bucket.
4. A flush attempt MUST emit diagnostic logs with point count and bucket context.

## 9. Shutdown and Reliability Requirements

1. On shutdown, the service MUST stop accepting new writes.
2. On shutdown, the service MUST complete queue writer and drain remaining items.
3. On shutdown, the service MUST attempt final flush of drained items.
4. `OperationCanceledException` and `ObjectDisposedException` during shutdown MUST be treated as expected lifecycle behavior and logged at non-error severity unless indicating actual data corruption.
5. Retry policy for failed writes SHOULD be bounded and configurable.
6. Runtime flush failures MUST be observable in logs/diagnostics with enough context to triage (exception, bucket, count).
7. The implementation MAY drop data after bounded retries are exhausted, but this policy MUST be documented and measurable.

## 10. Configuration Contract

The service configuration SHALL include at least:
- `Url` (Influx endpoint)
- `Organization`
- `Token`
- `DefaultBucket`
- `FlushIntervalSeconds` (default `10`)
- `MaxQueueSize` (default `500`)
- Optional retry settings

Configuration validation requirements:
1. `Url`, `Organization`, `Token`, and `DefaultBucket` MUST be non-empty.
2. `FlushIntervalSeconds` MUST be > 0.
3. `MaxQueueSize` MUST be > 0.
4. Invalid configuration MUST fail fast at startup.

## 11. Dependency Injection and Lifetime

1. The abstraction service implementation MUST be registered as singleton.
2. The flush worker MUST run as hosted background service or equivalent hosted lifecycle component.
3. The service MUST be available for constructor injection into `ILogic` implementations.
4. Startup of the host SHOULD NOT block on first successful Influx write.
5. The Influx-backed implementation SHOULD be registered by extension discovery via `IExtensionRegistration`.

## 12. Observability

The implementation SHALL expose diagnostics (log and/or counters) for:
- Enqueued measurements count
- Flush executions count
- Flush latency
- Flush failures
- Dropped measurements (if any)
- Queue depth snapshots

Sensitive values (for example token) MUST NOT be logged.

## 13. Security

1. Token handling MUST use standard configuration mechanisms and MUST NOT be emitted in logs.
2. TLS/HTTPS for Influx endpoint SHOULD be used in production deployments.
3. Access scope of token SHOULD be restricted to required organization and buckets.

## 14. Acceptance Criteria

The architecture is accepted only if all conditions are met:

1. Abstractions API has no dependency on `InfluxDB.Client` and exposes no Influx-specific types.
2. Service writes to InfluxDB OSS v2 via `InfluxDB.Client` only.
3. Measurements flush when interval reaches configured threshold (default 10s) even below max queue size.
4. Measurements flush when queue reaches configured max size (default 500) before interval.
5. Default bucket is used when measurement bucket override is absent.
6. Explicit measurement bucket override routes writes to override bucket.
7. Shutdown triggers queue completion, drain, and final flush attempt.
8. Expected shutdown cancellation/disposal exceptions are handled gracefully.
9. All required configuration values are validated at startup.
10. Influx-specific runtime code is isolated in a dedicated extension project and not embedded in Core.

## 15. Implementation Notes (Non-Normative)

- The legacy files under `tmp/Influx` are reference-only and SHOULD NOT be used as-is.
- Avoid the legacy anti-pattern of constructing a new client per write.
- Keep Influx runtime code in a dedicated extension project to reduce coupling and simplify future backend migrations.

## 16. Suggested Test Matrix (Non-Normative)

- Timer-triggered flush with low queue depth
- Size-triggered flush before timer elapses
- Mixed bucket routing (default + override)
- Graceful shutdown flush with pending queue
- Write failure with bounded retries and observable failure logs
- Config validation failures for missing required values

## 17. Alternatives Assessed and Rationale

### Alternative A: Implement directly in Core

Pros:
- Fewer projects and simpler initial wiring

Cons:
- Couples Core to a specific database technology and package
- Increases migration cost for future backend replacement
- Blurs responsibility boundaries between framework core and optional integrations

Decision:
- Rejected as target architecture.

### Alternative B: Put implementation helpers into Base

Pros:
- Shared convenience for logic modules

Cons:
- Base is intended for logic ergonomics, not network/database runtime infrastructure
- Risks leaking transport concerns upward into logic base classes

Decision:
- Rejected. Base remains convenience-only.

### Alternative C: Dedicated extension project + abstraction-first contract

Pros:
- Clean separation between stable contracts and backend implementation
- Lowest friction for replacing Influx with another store (for example TimescaleDB, ClickHouse, or file-backed sink)
- Aligns with existing extension registration/discovery architecture
- Keeps Core database-agnostic

Cons:
- Slightly more setup (project, registration, package references)

Decision:
- Accepted.

## 18. Revised Decision Record

1. Abstraction contracts for internal signal persistence SHALL remain in Abstractions.
2. InfluxDB OSS v2 implementation SHALL live in a dedicated extension project (`HomeCompanion.Integrations.Influx`).
3. Base SHALL provide optional convenience only and SHALL NOT own runtime persistence infrastructure.
4. Core SHALL stay database-agnostic and SHALL coordinate extension discovery/registration only.
5. Future database migrations SHOULD be implemented as additional extension projects reusing the same Abstractions contract.
