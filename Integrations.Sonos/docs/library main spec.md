# Sonos Integration Library Specification

## 1. Purpose

This library provides a reusable, service-oriented abstraction for controlling Sonos systems from HomeCompanion and other .NET applications. The library shall support local network control of Sonos players and groups, including playback control, topology management, and media queue/favorites/alarm operations.

The library is intended to be generic and reusable rather than tied to HomeCompanion-specific models. It shall expose behavior through explicit services and contracts that can be consumed by a host application or by higher-level HomeCompanion logic.

## 2. Scope

The library shall provide the following base capabilities:

- Sonos player discovery and topology inspection
- Group determination and group editing
- Playback control and state capture/restore
- Queue management
- Favorites management
- Alarm handling
- Media playback from an arbitrary URL, including mp3 streams
- Integration with other HomeCompanion services through clear abstractions

This specification is the minimum feature set. It is not meant to be exhaustive for every Sonos feature; however, the library architecture must be extensible enough to support additional Sonos functions without redesign.

## 3. Design goals

### 3.1 Reusability
The library shall be usable from HomeCompanion as well as other .NET applications without requiring HomeCompanion runtime types or infrastructure.

### 3.2 Service-oriented API
The library shall expose a collection of focused services rather than a single monolithic controller. This keeps responsibilities separated and enables partial use.

### 3.3 Local control first
The library shall prioritize direct Sonos local network control, using Sonos player/control capabilities and local discovery mechanisms. Any cloud-dependent functionality shall be optional and clearly separated.

### 3.4 Deterministic state handling
Operations that modify player state, grouping, playback, or alarms must be able to create a valid snapshot before change and restore it later when requested.

### 3.5 Extensibility
The library shall support future additions such as radio sources, presets, media search, or additional metadata access without breaking the public API.

## 4. Architectural model

The library shall be organized as a service collection with separate responsibilities.

### 4.1 Top-level entry point
The library shall expose a central coordinator or service provider abstraction, for example:

- `ISonosSystemService`
- `ISonosSystemManager`
- or similar

This entry point shall be responsible for:

- discovering Sonos systems on the local network
- enumerating players and groups
- providing access to individual device services
- creating runtime service instances for a selected Sonos system

### 4.2 Required service categories

The following service types are required:

> Note: the public Sonos model centers on players, groups, and households. A distinct, first-class "room" object is not the primary API concept; room names are user-facing labels for players and should be treated as metadata, not a separate domain entity.
>
> The source of truth for grouping is the Sonos coordinator/group topology, not heuristics inferred from room names, speaker suffixes, or left/right text labels. Any "left/right" or other naming conventions are presentation metadata and must not override the actual coordinator/member relationship.

#### `ISonosDiscoveryService`
Responsible for:

- finding Sonos systems on the network
- enumerating players
- resolving player names / room labels, player IDs, and coordinator information
- tracking changes in topology over time

#### `ISonosTopologyService`
Responsible for:

- determining current groups
- identifying coordinator and members of a group
- grouping players/speakers
- ungrouping players from a group
- retrieving state snapshots of player topology

#### `ISonosPlaybackService`
Responsible for:

- play / pause / stop / next / previous
- play a given URI or media item
- query current playback state
- set volume / mute / track position
- restore a previous playback state

#### `ISonosQueueService`
Responsible for:

- reading queue contents
- adding media items to queue
- clearing queue
- getting current queue item metadata
- managing playback from queue

#### `ISonosFavoritesService`
Responsible for:

- listing favorites
- playing a favorite by identifier or name
- adding/removing favorites where supported
- retrieving favorite metadata

#### `ISonosAlarmService`
Responsible for:

- listing current alarms
- creating, updating, deleting alarms
- enabling/disabling alarms
- retrieving alarm metadata and trigger state

#### `ISonosTransportService`
Responsible for low-level request execution and API abstraction, including:

- sending Sonos control requests
- handling timeouts and retries
- normalizing responses
- translating transport-specific errors into library exceptions

## 5. Core functional requirements

### 5.1 Device and system discovery
The library shall discover Sonos players on the local network and expose a normalized representation of the system.

Required behavior:

- identify each player and room
- identify the coordinator for a group
- identify the current group membership
- resolve unique device identifiers and player names
- detect player availability and offline state

Required result types:

- `SonosPlayer`
- `SonosGroup`
- `SonosSystem`
- `SonosHousehold` (optional but recommended if the API exposes household metadata)

These models shall include at least:

- player name / room label
- player id / UUID
- coordinator id
- zone or household membership metadata
- group membership
- playback state
- volume and mute
- availability status

### 5.2 Grouping and ungrouping
The library must support grouping and ungrouping players/speakers.

Required actions:

- determine which players are currently grouped
- group two or more players/speakers into a single group
- ungroup a player from its current group
- regroup one or more players based on a desired player list

Required semantics:

- group operation must be atomic from the library perspective
- if a requested grouping fails partially, the library shall return a result indicating the failure with enough detail to diagnose the issue
- the library shall expose the resulting group topology after grouping changes

### 5.3 State snapshot and restore
A core requirement is the ability to save a current Sonos status, change grouping or playback, and restore the original state.

The library shall provide a `SonosStateSnapshot` model containing at least:

- current household / group topology
- player membership within each group
- current source/URI
- current track metadata if available
- playback state (`playing`, `paused`, `stopped`)
- playback position
- volume and mute values
- favorites/queue context where available

Required operations:

- `CaptureStateAsync(...)`
- `RestoreStateAsync(...)`
- `CaptureAndRestore(...)` or equivalent transaction-style helper

Required semantics:

- restore should reapply the saved topology and room grouping
- restore should reapply playback mode and, if feasible, track position
- if a track is no longer available or if playback source is invalid, restore should fail gracefully and report the reason

### 5.4 Playback control
The library shall expose basic playback controls for all Sonos players.

Supported actions:

- play
- pause
- stop
- next
- previous
- seek to position
- set volume
- mute/unmute
- select source

The library shall support playing media from a URL, including an MP3 file URL.

Required behavior:

- accept a valid URL or media source
- validate or normalize the URI prior to sending to Sonos
- play the stream after optionally setting a preferred room or group
- report success or failure with a clear error result

Exemplary requirement:

- `PlayUrlAsync(playerId, "https://example.com/audio.mp3")`
- `PlayUrlAsync(roomName, uri)`

### 5.5 Queue operations
The library shall support queue interactions for Sonos players and groups.

Required functionality:

- retrieve current queue content
- enqueue one or more URIs or media sources
- clear queue
- play an item from the queue
- remove an item from the queue
- use queue state for restore scenarios

### 5.6 Favorites control
The library shall support playing supported favorite entries and reading metadata for them.

Required behavior:

- list favorites for the system or a player
- identify favorites by name or unique id
- play a favorite by identifier
- preserve favorite metadata for restore flows when relevant

### 5.7 Alarms
The library shall support Sonos alarm management.

Required behavior:

- list alarms for the system
- create or update alarm definitions
- delete alarm entries
- enable/disable alarms
- retrieve alarm metadata including time, recurrence, volume, and source

## 6. Key use cases

### 6.1 Group awareness and restoration
A HomeCompanion user may:

1. capture the current Sonos topology and playback state
2. ungroup or regroup speakers
3. play a temporary audio source
4. restore the previous entire state

This use case shall be available through a single high-level operation or a sequence of service calls that are individually testable.

### 6.2 Audio playback from URL
A HomeCompanion or host application may:

- play an MP3 file from a given URL
- target a specific room, player, or group
- optionally retain the previous state for later restore

### 6.3 Player grouping workflow
The library shall support a workflow equivalent to:

- read current groups
- ungroup selected players
- group selected players together
- verify final group membership
- restore prior group state if needed

## 7. Required public APIs

The public library API shall be strongly typed and asynchronous, with .NET-friendly service contracts and models.

### 7.1 Example service contract layout

```csharp
public interface ISonosSystemService
{
    Task<IReadOnlyList<SonosPlayer>> GetPlayersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SonosGroup>> GetGroupsAsync(CancellationToken cancellationToken = default);
    Task<SonosSystem> GetSystemAsync(CancellationToken cancellationToken = default);
}

public interface ISonosTopologyService
{
    Task<SonosGroup?> GetGroupForPlayerAsync(string playerId, CancellationToken cancellationToken = default);
    Task<GroupResult> GroupAsync(IEnumerable<string> playerIds, CancellationToken cancellationToken = default);
    Task<GroupResult> UngroupAsync(string playerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SonosGroup>> GetCurrentGroupsAsync(CancellationToken cancellationToken = default);
}

public interface ISonosPlaybackService
{
    Task<PlaybackResult> PlayAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> PauseAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> StopAsync(string playerId, CancellationToken cancellationToken = default);
    Task<PlaybackResult> PlayUrlAsync(string playerId, string mediaUrl, CancellationToken cancellationToken = default);
    Task<SonosPlaybackState> GetPlaybackStateAsync(string playerId, CancellationToken cancellationToken = default);
}

public interface ISonosStateRestoreService
{
    Task<SonosStateSnapshot> CaptureStateAsync(string playerId, CancellationToken cancellationToken = default);
    Task<RestoreResult> RestoreStateAsync(SonosStateSnapshot snapshot, CancellationToken cancellationToken = default);
}
```

This example is illustrative; the final library may use different but equivalent contracts. The important requirement is separation of responsibilities and the ability to capture and restore state.

## 8. Data model requirements

The library shall define typed models for the following concepts:

- `SonosPlayer`
- `SonosGroup`
- `SonosSystem`
- `SonosPlaybackState`
- `SonosStateSnapshot`
- `FavoriteEntry`
- `AlarmEntry`
- `QueueEntry`
- `PlaybackResult`
- `GroupResult`
- `RestoreResult`

`SonosPlayer` shall include user-facing player name / room label as metadata, but the library shall not define a separate first-class `SonosRoom` domain object unless a future Sonos API contract explicitly requires it. In other words, room semantics are treated as labels on players and not a distinct Sonos model entity.

Each model shall expose enough metadata for host applications to reason about state in a generic way without requiring direct Sonos XML/JSON parsing.

## 9. Error handling and result semantics

The library shall provide structured results instead of relying solely on exceptions for expected operational failures.

Required error semantics:

- device not found
- room not found
- player unavailable
- group operation rejected
- invalid URI or unsupported media type
- timeout or transport failure
- malformed Sonos response

Expected behavior:

- operations that fail should return a typed result with a clear status and message
- fatal transport errors shall still be surfaced as exceptions where appropriate
- all public operations shall support cancellation tokens

## 10. Reliability and robustness requirements

### 10.1 Timeouts and retries
The library shall support configurable timeouts and retry policies for Sonos control requests.

### 10.2 Changes in topology
Because group composition may change at runtime, the library shall refresh device and group state after each mutating operation.

### 10.3 Background state drift
The library shall not assume Sonos state remains unchanged after a command completes. State shall be re-read when needed to confirm the latest result.

## 11. HomeCompanion integration requirements

When used from HomeCompanion, the library shall integrate without hard-coding HomeCompanion-specific runtime objects.

The integration model shall be explicitly layered as:

- public Sonos integration library
- HomeCompanion service wrapper or adapter
- HomeCompanion logic components that use the wrapper

This keeps the Sonos library reusable and keeps local transport-specific adaptation outside the core library.

## 12. Non-goals

The following are explicitly out of scope unless added deliberately in a future extension:

- full music library indexing
- cloud-based Sonos account management
- Sonos app UI features
- unrelated smart-home control outside Sonos playback and topology

## 13. Acceptance criteria

The library is considered compliant with this specification if it can:

- discover Sonos players in the local network
- determine current group membership and player topology
- group and ungroup players/speakers
- capture a playback and topology snapshot
- play a media URL such as an mp3 file
- restore a prior state after a temporary change
- list and control alarms, favorites, and queue items
- expose this functionality through a service-oriented, reusable .NET API

## 14. Recommended implementation principles

- Prefer a narrow, explicit public API over one broad all-in-one class.
- Use typed result objects for state-changing operations.
- Separate low-level Sonos transport logic from domain logic.
- Keep room/group operations idempotent where practical.
- Store enough metadata in snapshots to meaningfully restore state.
- Represent Sonos topology as first-class domain objects instead of unstructured dictionaries.

## 15. Projects structure

The library splits in 3 projects:

- `Integrations.Sonos` - the main library project containing production code and public contracts.
- `Integrations.Sonos.Tests` - the unit test project containing automated tests for the library.
- `Integrations.Sonos.Abstractions` - the project containing shared abstractions for the Sonos integration.

The HomeCompanion specific integration and extension is placed in the `HomeCompanion.Integrations.Sonos` project, which references the main library and provides HomeCompanion-specific adapters and services in the folder `HomeCompanion.Integrations.Sonos/Extension`.

## 16. Test project placement

All unit tests for this library shall be placed in the `Integrations.Sonos.Tests` project. The test project is the canonical location for all automated validation covering the library's public contracts, state transitions, group operations, restore flows, and playback behaviors.

Tests shall not be mixed into the production library project. The library project shall remain focused on production code, while the `Integrations.Sonos.Tests` project owns verification and regression coverage.

## 17. Summary

The Sonos integration library shall become a reusable collection of services for Sonos control and state management. It must support player discovery, grouping, playback, queue/favorites/alarm operations, and restoration of prior state. The library shall be generic, strongly typed, and built around an extensible service architecture that can be consumed by HomeCompanion and other .NET applications.
