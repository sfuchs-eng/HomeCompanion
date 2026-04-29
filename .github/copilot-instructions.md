# HomeCompanion Project Guidelines

Home automation framework designed to complement KNX and OpenHAB with complex/parametrized logic. See [README.md](../README.md) for full overview.

## Build & Test

```bash
dotnet build HomeCompanion.slnx
dotnet test Tests/HomeCompanion.Tests.csproj
```

Unit tests must run **offline** — no connection to KNX, OpenHAB, MQTT, or InfluxDB required.

## Architecture

| Project | Role |
|---------|------|
| `HomeCompanion.Abstractions` | Core interfaces: `ILogic`, `IDiagnostic`, connectivity provider interfaces |
| `HomeCompanion.Base` | Base class `LogicBase` — implements `ILogic` with common functionality |
| `HomeCompanion.Core` | `LogicManager`, connectivity managers (KNX, OpenHAB, MQTT, InfluxDB) |
| `HomeCompanion.Logics` | Built-in `ILogic` module implementations |
| `HomeCompanion.Server` | Blazor Server app — entry point |
| `HomeCompanion.Tests` | NUnit test suite |
| `SRF.Network/` | Networking sub-solution — see its own [copilot-instructions.md](../SRF.Network/.github/copilot-instructions.md) |

Full dependency graph: [README.md § Structure](../README.md#structure).

The application is a strongly event based system, receiving, processing and sending events related to values of distributed data points.
This follows the general pattern of OpenHAB Items/Channels and of KNX group addresses with their objects in devices' memory.

## Breaking Changes

- Breaking changes can generally be made to any of the projects as they are being developed together. Backwards compatibility is not a concern at this stage.
- If a breaking change is made to `HomeCompanion.Abstractions` or `HomeCompanion.Base`, all other projects must be updated to compile with the new version before merging.
- If a breaking change is made to `HomeCompanion.Core`, `HomeCompanion.Logics`, or `HomeCompanion.Server`, the change must be merged and released before updating the other projects to compile with the new version.

## Logic Module Pattern

New logic modules go in `HomeCompanion.Logics` (or any project referencing `HomeCompanion.Base`):
1. Extend `LogicBase` (`HomeCompanion.Base`) — it implements `ILogic`
2. Register in DI; enable/disable per instance via configuration
3. Multiple instances can run simultaneously (e.g. production + testing side-by-side)

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
