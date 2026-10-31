# HomeCompanion Workspace Instructions

This file is the canonical agent guidance for the public `HomeCompanion` repository.

## Architecture boundary: logic templates

The project supports two valid templates for new logic implementations.

1. Public/shared logic template
   - Preferred for common functions and generic logic intended to be reusable.
   - Put the logic in the public `HomeCompanion` tree.
   - Keep configuration and runtime model in the public model layer; resolve runtime values through `IModelProvider` and the model value binder.
   - Keep transport-specific `IValueContainer` classes in the local project.
   - Example: the rain sensor flow (`RainSensorLogic` + `RainService`) is the reference pattern.

2. Local/special logic template
   - Preferred for very special, building-specific, or site-specific logic.
   - Put the logic in the local project and inject the relevant local `IValueContainer`(s) directly.
   - Use concrete local `IValue` objects and hard-coded local assumptions when needed.
   - This is the exception, not the default. Prompt the user for an explicit decision if a new logic is not clearly generic or local.

Mixed solutions are allowed only when the public logic is intentionally wrapped by a local adaptation layer. Keep the boundary explicit and avoid accidental coupling to transport-specific local values in the public project.

## Default decision rule

- Prefer the public/shared template for generic logic.
- Prefer the local/special template only for building-specific or site-specific logic.
- Do not hard-code local `IValueContainer` implementations into public logic.
- Do not move site-specific logic into the public library unless there is a deliberate architecture decision to make it reusable.

## Workspace boundary

- The public `HomeCompanion` code must stay independent of private/local project details.
- The private local workspace may reference public logic and add local value containers, mappings, and site-specific wiring.
- Keep the public/local split explicit in new code and documentation.
