# Sonos Integration TODO

## Current comparison against the specification

The public model and contracts are mostly in place (`SonosPlayer`, `SonosGroup`, `SonosSystem`, `SonosStateSnapshot`, result types, and the service interfaces). The main gap is that the actual library behavior is still mostly stubbed rather than implementation-complete.

The current implementation satisfies only the type-level skeleton of the spec. The following work remains before the library can be considered compliant with the specification.

---

## Must-fix implementation gaps

### 1. Replace placeholder transport logic with real Sonos request execution
- [x] Implement the actual request pipeline in `SonosTransportService` instead of returning static success values.
- [x] Add centralized request/response handling for Sonos control endpoints, including XML/JSON parsing, normalization of payloads, and conversion of protocol errors into `SonosErrorCode` values.
- [x] Add configurable retry policy and timeout handling using the options already defined in `SonosIntegrationOptions`.
- [x] Ensure `GetGroupsAsync`, `PlayAsync`, `PauseAsync`, `StopAsync`, `PlayUrlAsync`, `GroupAsync`, `UngroupAsync`, `CaptureStateAsync`, and `RestoreStateAsync` all call real logic and never silently return canned success values.
- [x] Map fatal transport failures to exceptions where appropriate, while keeping expected operation failures as structured `*Result` objects.

### 2. Implement real Sonos discovery and player normalization
- [x] Finish `SonosDiscoveryService` so it discovers Sonos devices from the local network and resolves all players, not just a best-effort SSDP pass.
- [x] Parse SSDP responses and `LOCATION` payloads reliably, including multiple players per household and multiple network interfaces.
- [x] Add a robust player model population: `Id`, `Name`, `CoordinatorId`, `HouseholdId`, `ZoneName`, `GroupMembership`, `Availability`, `Volume`, `IsMuted`.
- [x] Normalize player IDs consistently (`uuid:` stripping, lowercase canonicalization, deduplication) and ensure `Name`/room labels are treated as player metadata rather than a separate `SonosRoom` model.
- [x] Ensure `GetSystemAsync` returns a real `SonosSystem` object with discovered players and real group topology, instead of an empty or synthetic result.
- [x] Handle offline players explicitly and set `SonosAvailability.Offline` for missing/unreachable nodes.

### 3. Implement real topology and group management semantics
- [x] Implement `SonosTopologyService` against actual Sonos group topology, not just wrappers around static data.
- [x] Add a real `GetGroupForPlayerAsync` implementation that resolves membership from current topology and returns both the matching group and its coordinator.
- [x] Implement atomic grouping behavior in `TransportService.GroupAsync` and `TopologyService.GroupAsync` with result objects that report partial failure clearly.
- [x] Implement real ungrouping logic for a selected player and refresh topology after mutation.
- [x] Return `GroupResult` with `GroupId`, `PlayerId`, `Group`, and status details that reflect the actual post-operation state.
- [x] Ensure group membership is refreshed after each mutating action before returning to the caller.

### 4. Implement playback control and state polling according to the spec
- [x] Implement `StopAsync` in the transport layer as a real Sonos command; do not return a synthetic success response without dispatching a control call.
- [ ] Add support for `next` and `previous` in the transport/service surface if these are part of the required public API (or decide and implement the exact contract explicitly).
- [ ] Implement `seek`/position restore semantics and expose them as part of the public playback contract if required by the target feature set.
- [ ] Implement volume and mute adjustments with actual state re-read after mutation.
- [ ] Make `GetPlaybackStateAsync` return a real `SonosPlaybackState` built from the current Sonos device state and not a fixed stub value.
- [x] Validate that `PlayUrlAsync` accepts the required HTTP/HTTPS media URLs and rejects unsupported or malformed URIs with `SonosErrorCode.InvalidUri`.
- [x] Ensure playback operations support cancellation tokens and timeouts via the configured transport pipeline.

### 5. Implement queue support and remove placeholder data
- [ ] Replace the hardcoded fallback queue in `SonosQueueService.GetQueueAsync` with a real Sonos queue query.
- [ ] Implement queue retrieval, item metadata parsing, and indexing from the actual Sonos queue content.
- [ ] Implement `EnqueueAsync` by sending a real queue addition command and verifying the resulting queue state.
- [ ] Implement `ClearQueueAsync` and `PlayQueueItemAsync` as actual queue mutation/playback operations.
- [ ] Ensure queue state is included in state-capture and restore flows, as required by the spec.
- [ ] Add validation for invalid queue indexes and unsupported media URIs using structured results.

### 6. Implement favorites support as a real service
- [ ] Replace static favorite entries in `SonosFavoritesService` with a real favorites query against the system/player.
- [ ] Implement `GetFavoritesAsync` to load favorite metadata, IDs, names, type, and optional URI/metadata.
- [ ] Implement `PlayFavoriteAsync` to target the right favorite by ID or name and report success/failure with structured result data.
- [ ] Include favorite context in snapshot restore scenarios when relevant and preserve enough metadata for restoring state.

### 7. Implement alarm operations and validation
- [ ] Replace the in-memory alarm stubs in `SonosAlarmService` with real Sonos alarm listing, creation, update, and deletion operations.
- [ ] Implement `GetAlarmsAsync` with proper metadata parsing for `Id`, `Name`, `Enabled`, `Time`, recurrence, volume, and source.
- [ ] Implement `CreateAlarmAsync` and `UpdateAlarmAsync` with validation for required alarm fields and meaningful failure results.
- [ ] Implement `DeleteAlarmAsync` as an actual deletion command and return explicit false results for unsupported or failed operations.
- [ ] Ensure alarm metadata is captured in snapshot data where the specification expects it.

### 8. Implement `SonosStateSnapshot` capture and restore as a real transaction
- [ ] Expand `CaptureStateAsync` in the transport/system manager so it captures the required state: group topology, player membership, current source/URI, track metadata, playback state, volume/mute, queue/favorites context, and alarm context where available.
- [ ] Ensure `SonosStateSnapshot` includes enough information to restore the original topology and playback state without guessing.
- [ ] Implement `RestoreStateAsync` as an actual restore flow that re-applies topology, playback mode, and, when feasible, playback position and source.
- [ ] Gracefully handle cases where the track or source is no longer available and return `RestoreResult` with a clear error reason instead of pretending the restore succeeded.
- [ ] Add restore semantics for grouped/ungrouped rooms and ensure the operation is deterministic and idempotent where possible.
- [ ] Refresh system state after restore and confirm the final topology/playback state before returning success.

### 9. Standardize result/error semantics and cancellation
- [ ] Review all public methods for consistent `Success`, `Message`, and `ErrorCode` behavior.
- [ ] Ensure `DeviceNotFound`, `RoomNotFound`, `PlayerUnavailable`, `GroupRejected`, `InvalidUri`, `Timeout`, `MalformedResponse`, and `TransportFailure` are emitted consistently by the real transport layer.
- [ ] Add explicit handling for unsupported or malformed Sonos responses rather than silently continuing.
- [ ] Enforce cancellation-token propagation through discovery, playback, queue, topology, and restore calls.
- [ ] Ensure all public methods that fail in expected ways return typed results rather than throwing, while still allowing fatal transport exceptions when necessary.

### 10. Clean up architectural and API mismatches
- [ ] Confirm the library remains generic and does not hard-code HomeCompanion runtime objects or local-only assumptions.
- [ ] Keep the transport-specific adaptation outside the public library; the public library should depend on explicit service contracts and models only.
- [ ] Verify the naming and responsibilities match the spec: `ISonosSystemService`, `ISonosDiscoveryService`, `ISonosTopologyService`, `ISonosPlaybackService`, `ISonosQueueService`, `ISonosFavoritesService`, `ISonosAlarmService`, `ISonosTransportService`, and `ISonosStateRestoreService`.
- [ ] Decide whether `SonosHousehold` and `SonosSystem` remain optional/required based on the actual API contract and update the library structure and tests accordingly.
- [ ] Explicitly remove or replace any placeholder service behavior that was added only to make the initial tests pass but does not meet the library specification.

---

## Test work that must be added or corrected

### 11. Add failing regression tests for the spec, not just happy-path scaffolding
- [x] Add a test covering discovery of multiple players and household topology, including players with different coordinator/group membership.
- [x] Add a test proving `GroupAsync` fails cleanly when fewer than two valid players are supplied.
- [x] Add a test proving topology is refreshed after grouping and ungrouping operations.
- [x] Add a test proving invalid media URLs are rejected with `SonosErrorCode.InvalidUri` and a clear message.
- [ ] Add a test proving playback state capture includes group topology and the player’s current source/position.
- [ ] Add a test proving restore re-applies the previous topology and playback state.
- [ ] Add a test proving queue operations return actual queue entries and can play/remove/clear items.
- [ ] Add a test proving favorites and alarm endpoints return real metadata and can be created, updated, or deleted safely.
- [ ] Add a test proving timeout and malformed-response cases map to the correct result/error code.
- [ ] Add a test proving `SonosDiscoveryService` treats unavailable or offline devices as offline and does not claim they are active.
- [x] Keep all unit tests in `Integrations.Sonos.Tests` and avoid mixing production-library tests into the library project itself.

### 12. Review the existing tests and fix them to reflect actual spec behavior
- [x] Update the current Sonos tests to assert real library behavior instead of success based on stubbed data.
- [x] Remove assertions that only validate canned fake data when the real Sonos API behavior has not been implemented.
- [x] Ensure every test verifies the required public contract, not just that a fake transport returns an object.

---

## Priority order for implementation

1. [x] Real transport + request error model
2. [x] Discovery + player metadata normalization
3. [x] Topology/group operations
4. [ ] Playback + state capture
5. [ ] Queue + favorites + alarms
6. [ ] Restore transaction semantics
7. [ ] Regression tests and public API validation

## Definition of done

The Sonos library is ready only when all of the following are true:
- the library can discover players on the local network
- it can determine current group membership and coordinator topology
- it can group and ungroup players
- it can capture and restore prior state
- it can play media URLs and report failures deterministically
- it can list and manipulate queue, favorites, and alarms
- all public operations use typed results and cancellation-aware transport logic
- the tests in `Integrations.Sonos.Tests` validate the library against the specification instead of stubbed fake behavior
