# Architecture Specification: Logic Module Documentation Structure

**Date:** 2026-06-03
**Status:** Proposed
**Owner:** HomeCompanion.Logics + HomeCompanion.Base.Logics

## 1. Purpose

Define a consistent documentation architecture for logic modules so usage, behavior, and extension details are discoverable and maintained close to code.

This specification standardizes how README files in Logics subdirectories are used, linked, and kept in sync with implementation and architecture decisions.

## 2. Scope

In scope:

- Documentation requirements for logic modules in:
  - `HomeCompanion/Logics/**`
  - `HomeCompanion/Base/Logics/**`
  - local/private logic folders (for example `HomeCompanion.Local/Logics/**`) where applicable
- Required README structure for logic folders
- Cross-linking from architecture specs to logic READMEs
- Documentation update rules during feature implementation

Out of scope:

- End-user UI documentation
- External website/docs portal publishing
- Non-logic technical documentation conventions (covered by other docs)

## 3. Decision Summary (Locked)

1. Each logic folder SHALL have a colocated `README.md` describing behavior, configuration, and extension points.
2. Architecture specs in `docs/architecture/` SHOULD reference relevant logic README files directly.
3. Logic README content SHALL be implementation-oriented and versioned with code changes.
4. Behavior changes to a logic module MUST update that module README in the same change set.
5. If no module-specific README exists yet, a minimal bootstrap README MUST be created before or with the first substantial logic implementation.

## 4. Documentation Topology

## 4.1 Canonical Sources

- Architecture-level intent and constraints:

  - `docs/architecture/*.md`

- Module-level operational and development details:

  - `Logics/<ModuleFolder>/README.md`
  - `Base/Logics/<ModuleFolder>/README.md`
  - local logic repositories/folders where they follow the same pattern

Architecture specs describe the "why" and high-level boundaries.
Logic READMEs describe the "how" and practical usage/configuration details.

## 4.2 Link Strategy

Architecture spec pages SHOULD include a "Related Source Files" or "Related Docs" section with direct links to logic README files.

Example references:

- `Base/Logics/Shutters/README.md`
- Additional modules should follow the same pattern as they are added.

## 5. Required README Structure for Logic Modules

Each logic README SHOULD include at least:

- Purpose:

  - What the logic solves and key constraints.

- Feature Set and Current Implementation Status:

  - What is implemented now.
  - What is planned next.

- Architecture Overview:

  - Main components/classes and their responsibilities.
  - Boundary notes (event bus, value binding, persistence).

- Policy and Decision Rules:

  - Precedence and conflict resolution rules.
  - Safety/override behavior where relevant.

- Configuration Guide:

  - Relevant config objects and key fields.
  - Example JSON/YAML snippets when helpful.

- Runtime and State Behavior:

  - Persisted state sets and expiration/restore semantics.

- Testing and Extension Notes:

  - Where tests live.
  - How to safely extend behavior.

## 6. Lifecycle and Change Management

When implementing or changing logic behavior:

1. Update corresponding logic README in the same PR/commit.
2. If behavior affects cross-module architecture, update relevant architecture spec in `docs/architecture/`.
3. Keep examples and field names aligned with current code model.
4. Avoid documenting planned behavior as implemented; clearly separate "implemented" and "planned".

## 7. Quality Gates

Documentation quality requirements:

1. Markdown lint clean (no hard tabs, consistent headings/lists).
2. File links in docs should be valid workspace-relative paths.
3. Technical statements in README must match current code contracts.

## 8. Initial Adoption in Current Codebase

The following README is already aligned with this direction and serves as reference pattern:

- `Base/Logics/Shutters/README.md`

Future logic modules SHALL follow this template and cross-link pattern.

## 9. Acceptance Criteria

This architecture spec is considered adopted when:

1. New or modified logic modules include updated colocated README documentation.
2. Architecture specs for logic-heavy areas link to corresponding logic README files.
3. Review process checks documentation updates as part of logic behavior changes.
