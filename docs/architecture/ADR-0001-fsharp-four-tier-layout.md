---
id: ADR-0001
title: F# solution layout follows SDE's four tiers, with Aegis at the boundary
status: accepted
created: 2026-09-18
work_item: WI-0006
---

# ADR-0001 — F# solution layout

## Context

Vigila must be written in F# where practical (`VIG-GOV-009`), developed under
ROS (`VIG-GOV-006`) and SDE (`VIG-GOV-007`), and its domain model must not
depend on GitHub APIs, browser APIs, `localStorage`, or UI implementation
details (`VIG-GOV-015`).

`EchelonFoundry.Aegis.Core` was adopted for operational failure handling. Its
own guidance is to use it "at a boundary".

## Decision

### Four projects, one per SDE tier

| Project | Tier | May reference |
|---|---|---|
| `Vigila.Semantic` | 1 — Semantic Model | `FSharp.Core` only |
| `Vigila.Transition` | 2 — State Transition | Tier 1 only |
| `Vigila.Application` | 3 — Orchestration | Tiers 1–2, Aegis |
| `Vigila.Host.GitHub` | 4 — Host / Effects | Tier 3, Aegis |

Dependencies point downward only.

### Aegis enters at Tier 3, not Tier 1 or 2

Aegis handles operational failure at boundaries. Tier 1 says what can be true
and Tier 2 says what change is legal; neither has a boundary, so neither can
raise the kind of failure Aegis exists to express.

The distinction that matters: **a refused transition is a domain answer, not a
fault.** `Completed -> Cancelled` is not an error condition — it is the domain
correctly saying no. Routing it through fault handling would blur a decision
into a failure. A GitHub timeout is the opposite: nothing about the domain is
wrong, and the operation still did not happen.

So Tier 2 returns `Result<ItemStatus, TransitionRefusal>` and Tier 4 returns
Aegis faults, and the two never mix.

### Tier 1's isolation is structural, not conventional

`Vigila.Semantic` declares no `PackageReference` and no `ProjectReference` at
all. With nothing on its reference list it *cannot* reach GitHub, HTTP, the
browser or a serializer, even by accident. This mirrors the HelixNote evidence
cited in `.sde/architecture/FOUR-TIER-ARCHITECTURE.md`, where the semantic
project's reference list is what makes the guarantee hold.

`scripts/check-architecture.sh` enforces this in CI, before the build.

### FSharp.Core is pinned centrally

Aegis requires `FSharp.Core >= 10.1.400`, while the .NET 8 F# SDK implicitly
references `8.0.102` and NuGet then reports a downgrade (NU1605). Because
warnings are errors, this would fail the build. Resolved by setting
`DisableImplicitFSharpCoreReference` and pinning `10.1.400` through central
package management.

## Consequences

- A change that pulls infrastructure into the domain fails CI rather than
  review.
- Swapping GitHub for another backend (`VIG-PER-002`) means adding a Tier 4
  project; tiers 1–3 are untouched.
- The `Waiting` name exists as both an `ItemKind` and an `ItemStatus`, so call
  sites must qualify one of them. This is a real consequence of OQ-02 being
  unresolved, and the compiler surfaces it at every ambiguous use rather than
  letting it pass silently.
- Aegis is currently referenced but barely used — Tier 4 is a scaffold. The
  failure taxonomy is stated; the GitHub calls are not implemented.

## Alternatives considered

**Aegis in Tier 1 for uniform error handling.** Rejected: it would give the
domain a package dependency, breaking the structural guarantee above, and it
would encourage modelling domain refusals as faults.

**One project with folders per tier.** Rejected: folders are a convention a
compiler cannot enforce. Separate projects make the dependency direction a
build error.

**Defer the scaffold until the domain is designed.** Rejected: the adopted
package targets `net8.0` and had nowhere to live, so the alternative was
carrying an unreferenced dependency.
