# HomeCompanion Sonos Extension Integration Specification

## 1. Goal

This specification defines how the Sonos integration is embedded into HomeCompanion as a regular public HomeCompanion extension.

The Sonos integration shall stay in the public HomeCompanion repository and shall not depend on private or local project code. It shall be implemented as a first-class extension of the HomeCompanion server stack with:

- DI registration of Sonos abstraction services in the HomeCompanion server container
- an extension-level UI entry point in the HomeCompanion Server web UI
- page registration and route mapping that can be contributed by extensions
- core functionality delivered through the Sonos abstraction layer and the user-facing page

The driving requirement is that the Sonos extension must expose the library's real functionality to the user through the HomeCompanion Server web interface, with a strong focus on discovery, group topology, playback, and state restore.

## 2. Scope

This document covers:

- the architecture for embedding the Sonos library into HomeCompanion as a normal extension
- the required service-registration flow in HomeCompanion.Server
- the Blazor page and navigation model for extension pages
- the user-facing capabilities that must be available from the Sonos page
- the Definition of Done for the embedded integration

This document is deliberately public-facing and belongs to the shared HomeCompanion codebase, not to the local/private workspace.

## 3. Architectural target

### 3.1 General layering

The Sonos integration shall be structured in three layers:

1. Public Sonos library layer
   - `HomeCompanion.Integrations.Sonos`
   - contains the implementation of discovery, topology, playback, queue, favorites, alarms, and restore logic

2. Shared abstraction layer
   - `HomeCompanion.Integrations.Sonos.Abstractions`
   - contains `ISonos*` service contracts, result types, models, and shared DTOs

3. HomeCompanion extension layer
   - a public HomeCompanion extension that registers the services and contributes a Blazor page to the server app

This preserves the rule that the Sonos library is reusable outside HomeCompanion while the extension layer is responsible for HomeCompanion embedding.

### 3.2 Dependency direction

The dependency direction shall be:

- `HomeCompanion.Server` may depend on public `HomeCompanion.Integrations.Sonos`
- `HomeCompanion.Integrations.Sonos` depends on `HomeCompanion.Integrations.Sonos.Abstractions`
- `HomeCompanion` core does not depend on private/local setup code
- no local-only value containers or site-specific code are allowed in the public Sonos integration layer

## 4. Service registration requirements

### 4.1 Registration responsibility

HomeCompanion Core shall register the Sonos abstraction services as part of the HomeCompanion.Server startup flow.

This registration must happen through the extension mechanism already used for HomeCompanion integrations, not by ad hoc global static initialization.

Required behavior:

- `HomeCompanion.Server` shall discover Sonos extension registrations during startup
- the Sonos extension shall register all required `ISonos*` services in DI
- the registration must happen through `IExtensionRegistration`
- service lifetime must be consistent with server runtime behavior:
  - transient or singleton choices shall follow the Sonos library's needs and must remain consistent across runtime operations
  - the extension shall not instantiate or cache runtime state in the registration step itself

### 4.2 Required services

The Sonos extension shall register the services exposed by the abstraction layer, including at minimum:

- `ISonosSystemService`
- `ISonosDiscoveryService`
- `ISonosTopologyService`
- `ISonosPlaybackService`
- `ISonosQueueService`
- `ISonosFavoritesService`
- `ISonosAlarmService`
- `ISonosTransportService`
- `ISonosStateRestoreService`

If the implementation uses additional helper services, those must be public and registered as part of the same extension boundary.

### 4.3 Initialization semantics

Extension registration shall do only DI wiring. Runtime startup behavior and background discovery work shall be executed by the final service implementations or hosted services in the normal HomeCompanion lifecycle.

The Sonos extension must not bypass HomeCompanion startup conventions or create hardcoded server-side bootstrap code outside the extension registration flow.

## 5. Blazor page integration requirements

### 5.1 Extension page registration model

The Sonos extension shall register a page on HomeCompanion.Server. This page is the entry point for the Sonos web UI.

The extension framework must be expanded so that page entries can be contributed by extensions, instead of only hardcoding pages directly in the HomeCompanion.Server project.

A suitable extension contract is:

- a page descriptor model containing route, title, icon, category, and optional order
- a registration interface for extension pages
- a registry service that collects extension page entries during startup
- a nav-menu builder that uses the registry to render extension menu links

This requirement is not limited to Sonos. The framework should be generic enough for future integrations to add pages without modifying the server core.

### 5.2 Route mapping

The extensions framework may need to be expanded such that Blazor page routes can be mapped for pages provided by extensions.

Required behavior:

- extension-provided pages must register a route that resolves in the HomeCompanion.Server routing system
- the route must be discoverable and valid under the standard Blazor server routing model
- the server core must render extension pages without requiring a custom hardcoded route map in the main project
- the NavMenu shall be able to include extension links dynamically based on the registry data
- when the host application needs a local route boundary for app assembly routing, the host project must provide a thin local page stub with the route (for example `@page "/sonos"`) that delegates to the shared extension component while keeping the reusable Sonos UI in the extension assembly

The desired design is an extension capability analogous to:

- `IHomeCompanionPage` or `IExtensionPage`
- `IHomeCompanionPageRegistry`
- a `RegisterPage` or `AddPage` call from extension registration

The server should collect those entries and use them to render the menu and route map at startup time.

### 5.3 Navigation model

The Sonos extension page shall be placed in the HomeCompanion server nav menu as a standard extension menu item.

The UI contract shall support:

- a display title such as "Sonos"
- an icon and ordering value
- route discovery and selection without server core changes for each new extension
- expansion for subpages or grouped functionality when needed

The nav menu should not be a static hardcoded list only for the built-in server pages. It shall be extended by injectable page registrations from extensions.

## 6. User-facing Sonos page requirements

The Sonos extension page shall expose the core functionality of the library to the user via the HomeCompanion.Server web UI.

The page shall be a real operational control interface, not only a passive status page.

### 6.1 General page behavior

The Sonos page shall support:

- discovery of the Sonos system and all players visible on the local network
- display and refresh of topology, group memberships, and player metadata
- grouping and ungrouping controls for speakers and players
- playback controls for play, pause, stop, volume, mute, next, previous, and seek if supported by the device model
- playback from a user-supplied URI, with explicit validation and feedback for invalid or unsupported URLs
- state snapshot capture and restore, so a user can temporarily change topology or playback and then restore the original system state

### 6.2 System discovery and topology view

The topology view is of particular interest and is a primary requirement.

The page shall render a structure close to the underlying Sonos system model:

- household or system identifier
- player list with room names and unique identifiers
- coordinator information
- current group membership
- group members / players in each group
- availability state and playback metadata

The UI shall provide clear affordances to:

- select players
- inspect groups
- see which player is the coordinator
- change grouping by regrouping selected players
- ungroup selected players
- refresh topology state after a change

The topology view shall be easy to read and reliable when group membership changes at runtime.

### 6.3 Playback controls

The Sonos page shall allow direct playback operations for a selected player or group:

- play
- pause
- stop
- volume change
- mute / unmute
- play from URL
- restore saved state

The page must not treat Sonos playback as a passive display only. It must support the live operational functions the library already provides.

### 6.4 Play from URI

A core use case is playing arbitrary audio URLs, including MP3 streams.

The UI shall provide:

- a field for a media URI
- validation of the URL format
- selection of target player or group
- a button to play the media
- success/error feedback using the result contract from the Sonos library

This is a user-facing equivalent of `PlayUrlAsync` and must be implemented in the page layer using the abstraction services.

### 6.5 Save / restore state

The Sonos page shall support state capture and restore.

Required flows:

- capture the current Sonos state for a player/group/system
- save the snapshot locally in memory or in a server-side session service
- change grouping or playback
- restore the saved state

This is essential for temporary use cases such as:

- ungrouping speakers to play a temporary stream
- restoring the original room group after a broadcast or test audio play
- preserving playback position and topology before a user-initiated change

The UI should clearly indicate when a snapshot was captured, what it contains, and whether restore succeeded or failed.

## 7. HomeCompanion extension contract requirements

The Sonos extension shall conform to public HomeCompanion extension conventions.

### 7.1 Required extension registration

The Sonos extension shall implement the extension registration contract and wire the services in the public registration flow.

### 7.2 Required UI integration

The Sonos extension shall contribute:

- one page entry in the HomeCompanion.Server navigation menu
- one route-backed Blazor page for the Sonos functionality
- one thin local host-page stub with the final route when the hosting app needs to expose the page through its own AppAssembly boundary
- optional supporting components for grouped topology and playback controls

The reusable Sonos UI itself should remain in the extension assembly, while the host app may expose a minimal route wrapper to satisfy the app-level route resolution and keep the public extension contract decoupled from the local project.

### 7.3 Required injection model

The page system must allow a HomeCompanion extension to provide pages without direct modification of the server core menu file. The page route shall be discovered by the server via the registry mechanism.

## 8. Definition of done

The Sonos integration is considered complete only when all of the following are true:

- the Sonos services from the abstractions layer are registered through HomeCompanion.Server as part of the extension startup flow
- the Sonos extension contributes a route-backed page to the HomeCompanion.Server UI
- the server navigation model supports injectable pages from extensions
- the Sonos page can discover the Sonos system on the local network
- the page displays the current topology including groups, players, coordinator relationships, and room/player metadata
- the user can change grouping and ungrouping operations from the UI
- the UI supports playback actions such as play/stop/pause and volume controls
- the UI supports play from a URI including MP3/stream media
- the UI supports state capture and restore, including recovering the previous Sonos topology and playback state
- errors from the Sonos library are surfaced clearly to the user with a result-oriented message model
- the integration remains fully in the public HomeCompanion repository and is not coupled to the private local project

## 9. Non-goals

This document does not require full cloud integration or Sonos app parity. The integration focuses on the controllable local network behavior that is necessary for HomeCompanion to manage Sonos speakers and playback in a robust way.

Other features such as a full music library browser, cloud account management, or Sonos app-specific UX are intentionally out of scope unless added as a separate feature extension.

## 10. Acceptance summary

The public HomeCompanion Sonos extension shall behave like a normal HomeCompanion integration:

- it registers its services in the server via the extension mechanism
- it adds a UI page to the server through an injectable page system
- it gives the user access to the library's key operational features
- it focuses on discovery, groups, speaker topology, playback controls, and state restore
- it remains aligned with the public architecture and does not leak private/local implementation detail into the shared project

This integration shall be treated as a regular HomeCompanion extension and must be implemented in the public HomeCompanion codebase, not in HomeCompanion.Local.
