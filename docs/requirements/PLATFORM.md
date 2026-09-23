---
id: REQ-PLAT
title: Required shared platform dependencies and ownership boundaries
status: draft
version: 0.1.0
created: 2026-09-23
updated: 2026-09-23
sources: v0.5 §144–§151
---

# Shared platform requirements

Vigila v1 is implemented on the Echelon shared platform rather than recreating
cross-application infrastructure locally. Aegis owns unexpected operational
failure handling at architectural boundaries. Forma owns reusable application
presentation contracts. Folio owns reusable print and PDF document-layout
intent. Limen remains the browser interaction boundary and Vigila/Ordo state
remains the semantic authority.

These dependencies do not move business rules out of Vigila. They remove
duplicated infrastructure while preserving explicit ownership boundaries.

## Mandatory platform adoption

#### VIG-PLAT-001 — Aegis, Forma, and Folio are required implementation dependencies
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §144

Vigila MUST use Aegis for unexpected operational failures, Forma for reusable
screen UI presentation, and Folio for printable/PDF document composition where
Vigila emits a printable or shareable document. They are architectural
dependencies, not optional examples.

#### VIG-PLAT-002 — Shared capabilities must not be silently reimplemented
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §144

When Aegis, Forma, or Folio already provides the required capability, Vigila
MUST consume that capability rather than copy, fork, or independently recreate
it. A justified exception MUST be recorded as an architecture/decision record
and MUST identify the missing shared capability.

#### VIG-PLAT-003 — Dependency versions are explicit and reproducible
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §144

Shared platform dependencies MUST be pinned to an exact released version or an
immutable artifact/commit. Vigila MUST NOT depend on a moving repository branch
or a floating version range for its application baseline. A platform upgrade is
an explicit application change with verification evidence.

## Aegis installation and configuration

#### VIG-PLAT-010 — Aegis Core is required
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §145

The .NET application/host tiers that own operational boundaries MUST reference
`EchelonFoundry.Aegis.Core`. The v1 baseline is version `1.0.0` unless an
explicit later upgrade is recorded. Pure semantic and transition tiers MUST
remain free of Aegis dependency unless they themselves become an operational
boundary.

#### VIG-PLAT-011 — Integration-specific Aegis packages are used when applicable
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §145

If a Vigila .NET boundary calls GitHub directly, it MUST use the compatible
Aegis GitHub integration mapping rather than inventing a second GitHub failure
taxonomy. Where GitHub calls are executed through Limen/browser effects, the
observed effect outcome MUST be translated into Vigila/Aegis fault semantics at
the application boundary without exposing the GitHub token.

#### VIG-PLAT-012 — Aegis is configured once and validated at startup
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §145

Aegis MUST be configured at the application composition/startup boundary with
an application identity, version where available, explicit sinks, redaction
rules, and persistence posture. Configuration MUST be validated with Aegis
bootstrap validation before it is trusted. Vigila MUST NOT rely on an ambient
global singleton that hides configuration.

## Aegis usage

#### VIG-PLAT-020 — Aegis guards architectural boundaries
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §146

Unexpected operational failure at an architectural boundary MUST pass through
Aegis. Boundaries include network calls, GitHub API/repository operations,
browser/WASM interop, durable storage, file/record parsing, schema migration,
and any other external operation capable of failing independently of Vigila's
domain rules.

#### VIG-PLAT-021 — Boundary capture uses the standard Aegis capture surface
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §146

Synchronous and asynchronous boundary operations MUST use the standard Aegis
capture APIs appropriate to the operation. True top-level process or dispatcher
boundaries MUST use the Aegis guard surface where applicable. New bare
`try/with` or equivalent generic exception swallowing MUST NOT replace Aegis
at a declared operational boundary.

#### VIG-PLAT-022 — Domain outcomes stay in the domain model
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §146

Aegis MUST NOT be used for outcomes that Vigila's typed domain model already
represents, including validation refusal, illegal transitions, ambiguity,
conflict, or other expected business outcomes. Domain refusals remain typed
domain/application results. Aegis handles unexpected operational failure.

#### VIG-PLAT-023 — Programming defects fail loudly
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §146

Programming defects MUST NOT be converted into ordinary recoverable operational
faults merely to keep the application running. Defects that Aegis classifies as
programming failures MUST retain fail-loud behavior. Cancellation MUST be
handled according to Aegis cancellation semantics and MUST NOT automatically be
reported as a fault.

#### VIG-PLAT-024 — Fault classification is stable and integration-owned
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §146

Every captured operational failure MUST have a stable machine-readable fault
code, category, severity, availability impact, recovery posture, and safe user
message. An integration with its own typed failure model SHOULD define one
translation mapping for that integration rather than repeat ad hoc
classification at every call site.

#### VIG-PLAT-025 — Aegis context is privacy-classified and redacted
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §146

Aegis context, diagnostics, sinks, exports, breadcrumbs, and fault presentation
MUST preserve Vigila's credential and privacy requirements. GitHub tokens and
other secrets MUST never be recorded. Context values MUST use the appropriate
privacy classification and redaction rules before they can reach a sink.

#### VIG-PLAT-026 — Fault boundaries are declared
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §146

Vigila MUST maintain a machine-readable declaration of its Aegis fault
boundaries, ownership, expected fault-code families, and whether each boundary
is guarded. A newly introduced external boundary MUST update that declaration in
the same change or provide a recorded not-applicable reason.

#### VIG-PLAT-027 — Aegis behavior is tested through replaceable sinks
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §146

Tests for fault capture and classification MUST use deterministic replaceable
Aegis sinks such as the collector sink. Tests MUST NOT depend on console text,
real diagnostic files, or live network sinks to prove Aegis behavior. Boundary
tests MUST prove that expected domain refusals are not incorrectly converted
into Aegis faults.

## Forma installation

#### VIG-PLAT-030 — Forma is pinned as the application design-system dependency
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §147

Vigila MUST consume Forma through the `@echelon-foundry/design-system`
package contract. The current application baseline is Forma `0.2.0`. Until a
published npm package is the chosen canonical source, the immutable v0.2.0
release artifact is the dependency source. Vigila MUST NOT copy Forma CSS into
its own source tree.

#### VIG-PLAT-031 — Vigila loads the canonical Forma presentation surface
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §147

Vigila SHOULD load Forma's complete `all.css` surface unless a measured reason
justifies selective imports. Selective imports MUST still come from the pinned
Forma package. Application CSS MAY compose page-specific layout but MUST NOT
duplicate or fork canonical Forma component styling.

## Forma usage

#### VIG-PLAT-032 — Existing Forma patterns are the first UI choice
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §148

Before adding or changing a screen control, Vigila MUST search the Forma
component/pattern catalog and use the existing canonical contract where one
fits. Application-local controls that duplicate an existing Forma pattern are a
defect unless a recorded exception explains why the shared contract cannot be
used.

#### VIG-PLAT-033 — Forma authoring wrappers preserve native semantics
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §148

Where Forma defines an inert `<ef-*>` authoring wrapper, Vigila MUST use the
documented wrapper and canonical native HTML within it. Vigila MUST NOT register
Forma wrappers with `customElements.define()`, move native semantics onto the
wrapper, or remove required label, form, dialog, ARIA, or state relationships.

#### VIG-PLAT-034 — Forma does not own application behavior or domain authority
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §148

Forma owns presentation contracts, responsive recomposition, tokens, and
accessibility presentation. Limen/Vigila owns runtime behavior beyond native
HTML. Vigila/Ordo state owns legal transitions, capabilities, obligations,
permissions, and domain invariants. Visual state MUST NOT become a second source
of domain truth.

#### VIG-PLAT-035 — Forma mobile and accessibility contracts are mandatory
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §148

Every Vigila screen using Forma MUST remain usable at the Forma 320 CSS px
minimum, preserve keyboard and non-pointer operation, retain visible focus,
support reduced motion and forced-colors behavior where the component contract
requires it, and preserve non-color state cues. A phone-specific fork of a
canonical component MUST NOT be created merely to make the layout fit.

#### VIG-PLAT-036 — Missing reusable screen patterns are fixed in Forma
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §148

When Vigila discovers a reusable visual/control need that Forma does not
provide, the gap MUST be recorded against Forma and implemented there when it
is genuinely cross-application. Vigila MAY use a bounded temporary
application-specific composition, but MUST NOT silently create a competing
shared component library inside Vigila.

#### VIG-PLAT-037 — Forma upgrades require application verification
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §148

A Forma version change MUST run Vigila's application build, browser tests,
accessibility checks, mobile checks, and inspections of the canonical patterns
Vigila consumes. Material migration decisions MUST be recorded through ROS.

## Folio installation

#### VIG-PLAT-040 — Folio is the required print component dependency
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §149

Vigila MUST consume `@echelon-foundry/print-components` for printable,
PDF, and document-style output. The current source baseline is Folio `0.3.0`.
Until a v0.3.0 package/release artifact is published, Vigila MUST pin an
immutable Folio commit containing that baseline rather than track `main`.
Once a package/release is canonical, Vigila MUST pin the exact version.

#### VIG-PLAT-041 — Folio registration and print CSS are consumed from the package
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §149

Printable Vigila surfaces that use Folio custom elements MUST load Folio's
registration module and `print.css` from the pinned dependency. Vigila MUST
NOT copy Folio element registration or print CSS into application source.

## Folio usage

#### VIG-PLAT-042 — Printable and shareable documents use Folio primitives
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §150

When Vigila produces a printable/shareable follow-up summary, review, export,
report, diagnostic bundle intended for a person, or other document-style
artifact, reusable page/document intent MUST be expressed with Folio primitives
where Folio provides them. Folio MUST NOT be used as the ordinary interactive
screen UI framework; that remains Forma's responsibility.

#### VIG-PLAT-043 — Semantic HTML and logical reading order come first
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §150

Printable Vigila documents MUST begin with semantic HTML and a logical reading
order. Folio custom elements are used only for reusable document/layout intent.
Meaningful content MUST remain understandable if custom elements have not
upgraded, and visual reordering MUST NOT contradict reading order.

#### VIG-PLAT-044 — Renderer capability is explicit
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §150

Vigila MUST identify the Folio capability tier required by a printable feature.
Portable browser behavior MUST NOT be described as pixel-identical. Chromium
margin-box/page-counter behavior MUST NOT be claimed as portable behavior.
Official deterministic PDF generation, where offered, MUST use a controlled
renderer contract and MUST record renderer/version metadata sufficient to
reproduce the output.

#### VIG-PLAT-045 — Vigila owns document meaning; Folio owns layout intent
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §150

Vigila owns document data, privacy decisions, labels, scoring/interpretation,
authorization, and semantic meaning. Folio owns reusable print/layout intent and
print CSS. Physical pagination belongs to the selected renderer. Folio
components MUST NOT become a storage or domain-state mechanism.

#### VIG-PLAT-046 — No application pagination engine
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §150

Vigila MUST NOT implement a JavaScript/WASM DOM-measure-and-repage loop to
replace Folio/browser pagination. It MUST prefer semantic structure, native
fragmentation, named pages, print CSS, and the renderer capability model.
Renderer limitations MUST be surfaced rather than hidden through unstable
layout heuristics.

#### VIG-PLAT-047 — Folio output is regression tested
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §150

Substantial printable Vigila outputs MUST have structural print tests. Official
deterministic PDF paths MUST have renderer-backed regression evidence
proportional to their risk. Tests MUST cover long content, multipage tables or
equivalent fragmentation risks when those constructs are present, and MUST
verify that sensitive Vigila/Aegis data is not leaked into printable output.

#### VIG-PLAT-048 — Missing reusable print primitives are fixed in Folio
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §150

When Vigila discovers reusable print/layout intent that Folio does not provide,
the gap MUST be recorded against Folio and implemented there when it is
cross-application. Vigila MUST NOT silently grow a parallel print-component
library.

## Cross-platform composition

#### VIG-PLAT-050 — Screen and print are projections of the same authoritative state
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §151

Forma screen views and Folio document views MUST be projections of the same
Vigila application/domain state. A print view MUST NOT recompute legality,
status, importance, obligations, or other domain meaning independently from the
screen/application projection.

#### VIG-PLAT-051 — Aegis presentation flows through the appropriate presentation system
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §151

Aegis faults intended for an interactive user MUST be rendered through the
app's Forma-based presentation components using safe presentation intent rather
than raw exceptions. Aegis information included in a human-readable printable
artifact MUST use Folio document composition and MUST obey the same redaction
and privacy rules. Technical and diagnostic detail MUST remain separable from
the safe user message.

#### VIG-PLAT-052 — Dependency presence and use are verifiable
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §151

CI/repository verification MUST be able to prove that the required Aegis,
Forma, and Folio dependencies are pinned and that application surfaces use
their canonical contracts. Merely listing a dependency without using it does
not satisfy this requirement. A required shared dependency that is absent,
incompatible, or bypassed MUST block completion of the affected work.

#### VIG-PLAT-053 — Shared-platform upgrades preserve ownership boundaries
**Level:** MUST · **Release:** v1 · **Source:** v0.5 §151

An upgrade to Aegis, Forma, or Folio MUST NOT move domain authority into the
shared package, move browser behavior into Forma/Folio, or duplicate shared
infrastructure back into Vigila. Upgrade review MUST explicitly check those
ownership boundaries as well as functional compatibility.
