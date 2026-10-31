# HomeCompanion Project Guidelines

Home automation framework designed to complement KNX and OpenHAB with complex/parametrized logic. See [README.md](../README.md) for full overview.

## Workspace Context

- This `HomeCompanion` repository is used as a submodule inside the private `HomeCompanion.Local` workspace.
- This file is the primary instruction pillar for development in `HomeCompanion`.
- A sibling workspace folder `BHw17Logic` exists only as legacy reference and must not be developed or modified.
- `HomeCompanion` and `HomeCompanion.Local` are greenfield solutions. Prefer clean architecture and direct redesign over compatibility layers.
- Backward compatibility is not required unless explicitly requested for a specific task.

## Build & Test

```bash
dotnet build HomeCompanion.slnx
dotnet test Tests/HomeCompanion.Tests.csproj
```

Unit tests must run **offline** — no connection to KNX, OpenHAB, MQTT, or InfluxDB required.

## Architecture

### Main Application

| Project | Role |
|---------|------|
| `HomeCompanion` | Core interfaces: `ILogic`, `IDiagnostic`, connectivity provider interfaces |
| `HomeCompanion.Base` | Base class `LogicBase` — implements `ILogic` with common functionality |
| `HomeCompanion.Core` | `LogicManager`, `ValuesManager`, connectivity managers (KNX, OpenHAB, MQTT, InfluxDB) |
| `HomeCompanion.Logics` | Built-in `ILogic` module implementations |
| `HomeCompanion.Server` | Blazor Server app — entry point |
| `HomeCompanion.Tests` | NUnit test suite |
| `SRF.Network/` | Networking sub-solution — see its own [copilot-instructions.md](../SRF.Network/.github/copilot-instructions.md) |

For full dependencies see the project files.

The application is a strongly event based system, receiving, processing and sending events related to values of distributed data points.
This follows the general pattern of OpenHAB Items/Channels and of KNX group addresses with their objects in devices' memory.

### Local folders

- `LocalConfig/`: Local configuration files (e.g. for the Blazor Server app) that are not checked into the main repository. This is where the local `HomeCompanion.json` file and other local configuration files go, as well as any other local configuration files needed for development or production.
- `LocalLogics/`: Local logic modules that are not checked into the main repository. This is where custom, non-public logic implementations can be placed. It's supposed to contain logic modules that are specific to the local setup and not intended for sharing or public use. This allows for local customization without affecting the main codebase.

As long as no packages for HomeCompanion are published, the `LocalLogics` should reference the `HomeCompanion.Server` project directly to use the latest code. Once packages might be published, there should
be a separate solution for local development that references the published packages. Some changes in HomeCompanion.Server might be needed to allow for consuming it as published package into a custom, local solution.

### Value Lifecycle And Routing
- `ValuesManager` is the single place that initializes `IValue` instances (`IValue.Initialize(...)`) at startup.
- Connectivity providers must not call `IValue.Initialize(...)`; they only map bus endpoints and publish/consume bus-related events.
- Inbound `ValueUpdateReceived` and `ValueWriteReceived` are routed centrally by `ValuesManager` using `Target`.

### Namespaces
- Generally, the namespace structure follows the project and folder structure (e.g. `HomeCompanion.Logics` for logic modules).
- There shall be no Abstractions namespace. Instead, the Abstractions project uses the same namespaces as the other projects (e.g. `HomeCompanion.Logics`) for its interfaces. This allows logic modules to depend on the Abstractions project without needing to reference a separate namespace. Same for `Events` and `Values`.
- The `Base` project name is stripped from the namespace, resulting e.g. in `HomeCompanion.Events` instead of `HomeCompanion.Events`. `Core`, `Logics`, `Server`, `Tests` and others keep the project name in the namespace.

## Breaking Changes

- Breaking changes can generally be made to any of the projects as they are being developed together. Backwards compatibility is not a concern at this stage.
- If a breaking change is made to `HomeCompanion` or `HomeCompanion.Base`, all other projects must be updated to compile with the new version before merging.
- If a breaking change is made to `HomeCompanion.Core`, `HomeCompanion.Logics`, or `HomeCompanion.Server`, the change must be merged and released before updating the other projects to compile with the new version.

## Logic Module Pattern

New logic modules go in `HomeCompanion.Logics` (or any project referencing `HomeCompanion.Base`):
1. Extend `LogicBase` (`HomeCompanion.Base`) — it implements `ILogic`
2. Register in DI; enable/disable per instance via configuration
3. Multiple instances can run simultaneously (e.g. production + testing side-by-side)

### Logic architecture templates

There are two valid templates for logic implementation. New logics must pick one deliberately and keep the boundary explicit.

#### 1) Public/shared logic template (preferred for reusable logic)

Use this for common, generic functionality intended to be shared or reused across sites.

- The logic lives in a public project such as `HomeCompanion.Logics` or a dedicated public logic library.
- The configuration and runtime model live in the public model layer (`HomeCompanion.Base.Model` / `HomeCompanion.Base.*` as appropriate).
- The logic resolves runtime objects through the model and `IModelProvider` / `ModelValueBinder`, not through a local `IValueContainer`.
- The logic depends on abstract services and `IValue` interfaces, not on local transport-specific containers or static MQTT values.
- The local project owns the actual `IValueContainer` definitions and transport wiring (`MqttValues`, OpenHAB values, etc.).
- This is the default pattern for common logic and should be used whenever the logic is conceptually generic rather than site-specific.

Example: the public rain sensor flow (`RainSensorLogic` + `RainService`) uses the model, binds runtime `IValue` references, and leaves the MQTT value container in the local project.

#### 2) Local/special logic template (preferred for building-specific logic)

Use this for very special, building-specific, or site-specific logic that is not intended for reuse.

- The logic lives in the local application project, not in the public HomeCompanion library.
- It injects the relevant local `IValueContainer`(s) directly and references the concrete `IValue` instances it needs.
- It may hard-code the relevant value names or local assumptions that are not part of the generic public model.
- This is appropriate for highly specific local automation, local site wiring, and custom building assumptions.
- The logic should remain isolated and not depend on public model internals unless there is a deliberate need for a local adaptation layer.

#### Decision rule

- Common functionality should follow template 1.
- Very special or site-specific automation should follow template 2.
- Mixed solutions are allowed only when a small local adaptation layer intentionally bridges a public core logic to local transport-specific values; the boundary must still be explicit and not accidental.
- Do not put a public logic in the shared project that hard-codes local `IValueContainer` classes or local MQTT values. Do not put local special logic into the public model layer unless it is intentionally designed as a site-specific extension.
- Prompt the user for an explicit decision if a new logic is not clearly generic or local.

## IValue principles

### KNX Group Addresses
- A KNX group address corresponds to a single `IValue` instance. This allows for a clear mapping between the KNX bus and the internal value management.
- Scenes are treated state-lessly, with the related IValue carrying the last called scene as its value.

## Code Style

- **Target**: .NET 10.0, C# latest, `Nullable` enabled, `ImplicitUsings` enabled
- **Naming**: PascalCase for all public members; `_camelCase` for private fields
- **Time**: Use `TimeProvider.System` — never `DateTime.Now`/`DateTimeOffset.Now` directly
- **Timestamps**: Prefer `DateTimeOffset` over `DateTime`; exception: external API contracts or serialized config files; time in config files is always local time.
- **XML docs**: Required on all public APIs — include `<remarks>` if non-obvious. Keep text short, concise, clear, and facts based with accurate references.
- **Collection initializers**: Favor [] over new() for empty collections (e.g. `new List<string>()` → `[]`); use collection initializers for non-empty collections (e.g. `new List<string> { "a", "b" }` → `["a", "b"]`)

## Testing

- Framework: NUnit 4.x (`NUnit.Framework` is globally `using`'d in `Tests/` — no import needed)
- Use `IDiagnostic` for in-app diagnostics available in both production and test instances
- See [README.md § Testing modes](../README.md#testing-modes) for the full test strategy

## Documentation

- All public APIs must have XML docs with `<summary>` and `<remarks>` if non-obvious
- README.md should be kept up to date with architectural changes, new features, and usage instructions
- For complex logic modules, keep a dedicated markdown file in `docs/` with details on configuration and behavior/functionality
- For architectural decisions, use ADRs in `docs/adr/` (Markdown) with clear descriptions of the solution, alternatives considered, and rationale for the decision
- For project key functionality, keep explanations and usage instructions in the project/library README.md files (e.g. `HomeCompanion.Logics/README.md` for the built-in logic modules) as well as XML docs on the relevant public APIs

## Special Notes

### Legacy Code

There's a legacy solution that `HomeCompanion` substitutes. It's not in the repository.
Some of its files are copied into the `tmp/` directory for reference. These files are not part of the project and should not be edited. They are only for reference during development.
They originate from the legacy solution that runs on .NET Framework 4.8 and is being replaced by this new .NET 10.0 solution. Architecture and code style may differ strongly and must be reconsidered.