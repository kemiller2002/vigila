---
id: REQ-SHARED
title: Echelon shared application capabilities
status: required
sources: platform policy, Forma consumer contract, Folio agent contract, Aegis usage contract
---

# Shared application capabilities

These requirements apply across every Vigila requirement when the named capability is relevant. A feature does not need to repeat them. An explicit, reviewable not-applicable rationale is required to opt out.

## VIG-SHR-001 — Shared capabilities are normative

**Level:** MUST · **Release:** v1

Vigila MUST consume Aegis, Forma, and Folio rather than reimplementing capabilities they already provide. Dependencies MUST be pinned to an explicit released version or immutable artifact. Floating versions and moving repository branches are prohibited as application baselines.

## VIG-SHR-010 — Aegis is required at operational boundaries

**Level:** MUST · **Release:** v1

Unexpected operational failure at GitHub, network, persistence, browser/WASM interop, parsing, file, migration, export/renderer, or other external boundaries MUST use Aegis. Expected domain outcomes remain typed Vigila/Ordo outcomes and MUST NOT be converted into operational faults. Raw technology exceptions MUST NOT cross declared integration boundaries.

Aegis configuration, privacy/redaction, sinks, recovery authority, unknown-effect handling, and deterministic translation tests MUST follow the current Aegis contract.

## VIG-SHR-011 — Aegis presentation uses shared UI

**Level:** MUST · **Release:** v1

When an Aegis fault is presented to a user, Vigila MUST map the safe Aegis presentation intent into application/Limen state and render it with the applicable Forma fault/error presentation component. Vigila MUST NOT maintain a separate competing error-component system when Forma covers the intent.

## VIG-SHR-020 — Forma is required for interactive UI

**Level:** MUST · **Release:** v1

All interactive Vigila browser UI MUST consume the pinned Forma package and use existing Forma patterns, tokens, and components before creating local equivalents. Forma presentation MUST NOT be copied or forked into Vigila. Native HTML owns semantics, Forma owns presentation, Limen owns browser interaction beyond native behavior, and Vigila/Ordo owns domain state and legal transitions.

Forma's responsive, 320px, keyboard, non-color-state, and accessibility contracts MUST be preserved. A missing shared pattern SHOULD be implemented in Forma rather than locally forked.

## VIG-SHR-030 — Folio is required for printable document surfaces

**Level:** MUST · **Release:** v1

Any Vigila feature that creates a printable, PDF, paginated, print-preview, report, handoff document, or other paper-oriented artifact MUST consume the pinned Folio package.

Existing Folio primitives MUST be used for document intent including headers, footers, page numbers, title/back pages, artwork/layers, columns, sidebars, breaks/keeps, metrics, findings, callouts, figures, tables, code, TOC, notes, and other shipped primitives. Vigila MUST NOT recreate those capabilities locally without a recorded Folio gap and approved decision.

If a release contains no printable/PDF/paginated artifact, Folio need not be installed solely for symmetry. The first applicable requirement activates this obligation.

## VIG-SHR-031 — Forma and Folio have separate responsibilities

**Level:** MUST · **Release:** v1

Interactive screen chrome, controls, navigation, dialogs, fault presentation, and responsive application UI use Forma. Printable/paginated document composition uses Folio. Document meaning remains Vigila-owned. Renderer-specific behavior MUST be declared and tested rather than presented as portable behavior.

## VIG-SHR-040 — Completion evidence

**Level:** MUST · **Release:** v1

A requirement using one or more shared capabilities is complete only when evidence proves the dependency is pinned and restored reproducibly, the shared capability is actually used, applicable Aegis boundary behavior is tested, applicable Forma browser/mobile/accessibility behavior is tested, applicable Folio print/PDF behavior is tested, and any exception is documented with rationale.
