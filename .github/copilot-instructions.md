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