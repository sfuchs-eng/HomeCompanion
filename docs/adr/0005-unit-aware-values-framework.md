# ADR-0005: Unit-Aware Values Framework

**Date:** 2026-09-05

## Context

Physical values in HomeCompanion (temperature, pressure, energy, speed, etc.) need first-class unit handling.
Before this decision, values were typed but unit semantics were implicit and scattered across integrations (for example KNX DPT display formatting) and ad-hoc parsing.

We needed a coherent model that supports:

- Strong typing of `IValue<T>` while staying bus-agnostic
- Parsing user/bus payloads with and without unit suffixes
- Reliable display formatting with unit symbols
- Smooth KNX/OpenHAB/MQTT inbound and outbound behavior
- Optional use of UnitsNet quantity types without forcing all values to be quantities

## Decision

Adopt a **hybrid unit-aware model**:

1. Keep `IValue<T>` generic and unconstrained.
2. Add optional unit metadata to `IValue` via `ValueUnitInfo? Unit`.
3. Use UnitsNet in Base/integration implementations for parsing, normalization, and formatting.
4. Keep display formatting (`IValue.Format`) separate from transport encoding (provider-specific).

### Contract and metadata

- Unit metadata is expressed as `ValueUnitInfo` in Abstractions, using UnitsNet naming conventions:
  - `QuantityName`, for example `Temperature`
  - `UnitName`, for example `DegreeCelsius`
  - optional `UnitSymbol`, for example `°C`
- Abstractions stay free of a direct UnitsNet package dependency.

### Parsing model

`ValueBase<T>.TryParseValue` now follows this strategy:

1. If `T` is a UnitsNet quantity type, parse with UnitsNet quantity parsing.
2. If `Unit` metadata exists and `T` is numeric, parse unit-suffixed text via UnitsNet and convert to configured unit.
3. If `Unit` metadata exists and text is numeric-only, interpret it in the configured unit.
4. Fall back to `IParsable<T>`, enum parsing, and convertible fallback behavior.

### Formatting model

`IValue.Format` order of precedence:

1. Bus mapping formatter (`FormatValueForDisplay`) when available
2. Unit-aware formatting in Base (quantity-aware and scalar+unit-aware)
3. Generic CLR formatting fallback

### Integration behavior

- **KNX**:
  - Inbound decoded values are normalized through target value parsing where needed.
  - Outbound quantity values are converted to scalar magnitudes before DPT encoding.
- **OpenHAB**:
  - Inbound states are parsed through target `IValue.TryParseValue` (unit-aware path included).
  - Outbound writes append configured display unit where unit metadata exists.
- **MQTT**:
  - Inbound raw/json-scalar strings are parsed through target `IValue.TryParseValue`.
  - Outbound raw payload formatting appends unit symbols for scalar+unit values and uses quantity formatting for UnitsNet quantities.

## Consequences

### Benefits

- Unit semantics are now explicit and discoverable on all values.
- Parsing and formatting rules are centralized in the value framework.
- Integrations reuse the same parsing contract instead of custom per-provider heuristics.
- Both scalar+unit and quantity-as-`T` models are supported.

### Trade-offs

- `IValue` is a breaking contract change due to the new `Unit` property.
- Some payloads may now serialize with explicit unit suffixes depending on provider mode.
- Quantity conversions rely on valid `ValueUnitInfo` configuration.

## Guardrails and invariants

- Values remain bus-agnostic.
- `ValuesManager` remains the central initializer and router.
- Connectivity providers continue to own transport encoding/decoding.
- Display formatting is not used as transport format contract.

## Migration notes

- Existing unit-less values continue to work unchanged (`Unit == null`).
- To make a scalar value unit-aware, set `Unit` metadata on the value instance.
- Quantity-typed values (`IValue<Temperature>`, etc.) are optional and should be used where strong domain quantity semantics are preferred.
