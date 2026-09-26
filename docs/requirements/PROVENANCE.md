---
id: REQ-PROV
title: Actor identity and provenance across the Echelon boundary
status: draft
version: 0.1.0
created: 2026-09-26
updated: 2026-09-26
sources: Praxis DF-ROS-2026-A036, DF-ROS-2026-A037, RQ-ROS-2026-A001..A019 (kemiller2002/praxis@c2657efb4d54f11d0fd0617cc1bcd5b8418601d5, contract revision 1.1); echelon-registry REG-PROV-006..REG-PROV-008 (commit 1788a35); work items FEAT-ECHELON-PROVENANCE, FEAT-ECHELON-PROVENANCE-R1
provenance:
  contributions:
    EXE-20260926T081409758Z-615c839a:
      operations: [created, modified]
      at: 2026-09-26T08:17:36.692Z
      last: 2026-09-26T08:34:29.209Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Add VIG-PROV requirements carrying the Praxis provenance contract (FEAT-ECHELON-PROVENANCE)"
    EXE-20260926T085500134Z-ac7e0976:
      operations: [modified]
      at: 2026-09-26T09:02:58.793Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Adopt Praxis provenance contract revision 1.1 and registry REG-PROV-008 v1 keys (FEAT-ECHELON-PROVENANCE-R1)"
---

# Actor identity and provenance

Vigila records follow-ups that other Echelon systems and agents raise, and that
agents and people later handle, resolve and review. Each of those actors plays a
different part, and one `Actor {type, name}` field per record cannot say which.
This document makes Vigila a *carrier* of the Praxis provenance contract. It
does not define a second identity model.

**Authority.** Praxis owns the meaning of actor, execution, contribution,
operation, lineage and `unknown`
(`docs/agent-provenance.md`, `DF-ROS-2026-A036`, `DF-ROS-2026-A037`). These
requirements say only how Vigila stores, receives and appends that contract.
When they appear to disagree with Praxis, Praxis governs, and the conflict is a
defect here.

**Source.** Unlike the other requirement documents, this area does not derive
from `input-documents/`. It derives from the cross-system Echelon provenance
decision named in the front matter and from the owner's instruction for work
item `FEAT-ECHELON-PROVENANCE`. It extends `VIG-DOM-035`, `VIG-DOM-037`,
`VIG-AGT-035`, `VIG-AGT-045` and `VIG-PER-020`..`VIG-PER-023`; it replaces none
of them.

## Identity model

#### VIG-PROV-001 — Praxis is the only identity model
**Level:** MUST · **Release:** v1 · **Source:** DF-ROS-2026-A037 §1, RQ-ROS-2026-A001, RQ-ROS-2026-A002, RQ-ROS-2026-A015

Vigila MUST represent agent and system identity with the Praxis actor
(`{kind, id, provider?, model?, runtime?}`) and the `praxis.provenance/1`
interchange block. It MUST NOT redefine what an agent, execution,
contribution, operation or `unknown` means, and MUST NOT copy and modify the
Praxis schemas.

#### VIG-PROV-002 — An item carries its own provenance block
**Level:** MUST · **Release:** v1 · **Source:** RQ-ROS-2026-A004, RQ-ROS-2026-A009, DF-ROS-2026-A037 §1

An item MAY carry a `provenance` block (`praxis.provenance/1`). When present it
is the authoritative record of who contributed to the item. The existing
`createdBy`/`actor` `{type, name}` fields and `createdVia` remain as a display
and legacy projection (`VIG-PROV-015`); they MUST NOT be read as the identity
when a block exists.

#### VIG-PROV-003 — Roles are distinct and never collapsed
**Level:** MUST · **Release:** v1 · **Source:** RQ-ROS-2026-A014, RQ-ROS-2026-A013, RQ-ROS-2026-A002

Vigila MUST be able to express, as separate contributions each keyed by the
execution that produced it:

| Part played | Operation | Where it is recorded |
|---|---|---|
| Actor that raised the follow-up (the generating system or the requesting agent/human that invoked `followup.create`) | `created` | the item's `provenance` |
| Agent that discovered the underlying issue | `discovered` | the provenance received with the request (`VIG-PROV-010`) |
| Vigila, when it turns a request into an item | `transformed` | the item's `provenance` (`VIG-PROV-012`) |
| Agent or person that later handled the item | `modified`, `remediated` | the item's `provenance` |
| Agent or person that resolved it | `resolved` | the item's `provenance` |
| Person who reviewed it | `reviewed` | the item's `provenance` |

Two executions of the same agent MUST remain two contributions. A transporting
or later actor MUST NOT overwrite the original actor.

#### VIG-PROV-004 — Contribution keys
**Level:** MUST · **Release:** v1 · **Source:** RQ-ROS-2026-A013, DF-ROS-2026-A037 §3

A contribution is keyed by `EXE-…` (a Praxis execution), `EXT-<system>.<run-id>`
(a run in another Echelon system) or `CTB-…` (a non-agent contribution outside
any execution). An agent contribution MUST be keyed by an execution. When only
an operation id is known, the key MUST be `EXT-op.<operationId>`. Vigila MUST
NOT invent `EXE-` identifiers. An id carried inside a key MUST be escaped
injectively (contract revision 1.1): `_` and every character outside
`[A-Za-z0-9.-]` become `_xx` per UTF-8 byte, so `op 1` is `EXT-op.op_201` and
two different ids never share a key.

## Receiving and appending

#### VIG-PROV-005 — Append-only contributions
**Level:** MUST · **Release:** v1 · **Source:** RQ-ROS-2026-A004

Operations that change an item (status change, resolution, note, review) MUST
append a contribution when an actor and an execution (or operation id) are
supplied. They MUST NOT replace, delete, reorder or rewrite another
contribution. The same key merges operations and advances `last` only when the
actor agrees; re-attribution is refused, as is a second or late `created`.
Appending an identical contribution is a no-op. Under contract revision 1.1 a
same-key merge also keeps the incoming entry's unknown fields (the existing
entry wins on conflict) and sets `last` to the later of the two times; an actor
whose identity is `unknown` cannot extend an entry a known actor holds; and an
append MUST NOT return a block that would itself be malformed (a credential, a
contribution dated before the creation, a second originator).

#### VIG-PROV-006 — Deterministic receiving verdicts
**Level:** MUST · **Release:** v1 · **Source:** RQ-ROS-2026-A015, DF-ROS-2026-A037 §2

Every received or loaded block MUST be classified as:

- `supported` — preserved, including fields this version does not model;
  operation codes it does not know are tolerated and reported;
- `unsupported` (another major version) — carried verbatim and never
  interpreted, merged into or appended to;
- `malformed` — including a key, code, kind or tag that matches only up to a
  trailing newline, a timestamp that is not calendar-valid (year 0001-9999, no
  February 30, no `24:00`; ordered at millisecond precision), and any field
  present as JSON `null`, which never means absent — rejected at the boundary
  with a structured error
  (`ValidationFailed`, `VIG-AGT-050`) that names the problems. A malformed block
  MUST NOT be dropped or repaired silently.

#### VIG-PROV-007 — Persisted schema version 2
**Level:** MUST · **Release:** v1 · **Source:** VIG-PER-020, VIG-PER-021, VIG-PER-022, VIG-PER-023, RQ-ROS-2026-A007

A persisted item that carries anything schema version 1 cannot express — a
`provenance` or `receivedProvenance` block, a history entry or note linked to a
contribution, or an actor of type `unknown` — MUST be written with
`schemaVersion: 2`. Every other item MUST continue to be written as version 1,
so records unaffected by this change stay byte-compatible with older builds.
Readers MUST accept versions 1 and 2. No migration runs: nothing is rewritten
until a real operation changes an item. (`VIG-PER-023` is satisfied trivially:
there is no data transformation, and version progression per record is
monotonic.)

#### VIG-PROV-008 — Legacy records stay valid and unattributed
**Level:** MUST · **Release:** v1 · **Source:** RQ-ROS-2026-A007, DF-ROS-2026-A036

A version 1 item without provenance MUST remain readable and MUST read as
*unattributed*. Vigila MUST NOT infer, backfill or invent an actor, execution or
`EXE-` id for it from its `{type, name}` fields or from anything else. When a
legacy item is later changed by an attributed operation, it gains a block whose
first contribution is that operation (Praxis "origin unknown"); it never gains a
`created` contribution retroactively.

## Integration boundary

#### VIG-PROV-009 — `followup.create` intake
**Level:** MUST · **Release:** v1 · **Source:** RQ-ROS-2026-A009, RQ-ROS-2026-A016, VIG-AGT-035, echelon-registry `followup.create` v1

Vigila MUST accept `followup.create` with an `echelon.execution-envelope/v2`,
and with a `v1` envelope mapped losslessly by the Praxis rules
(`actorFromEnvelopeV1`: `system` → `automation`; an unknown value → the literal
`unknown`; not-applicable omitted for humans; `model`/`runtime` `unknown`;
key `EXT-run.<runId>` or `EXT-op.<operationId>`, namespaced as
`EXT-run.<repository>.<runId>` when `source.repository` is known, with `.` in the
repository also escaped, exactly as echelon-registry REG-PROV-008 defines).
Envelopes MUST meet the registry schemas: a property outside the schema is
rejected, except `x-...` extension properties on v2. The invoking actor is taken
only from the envelope; nothing is guessed from ambient signals. The resulting
item's `provenance` records the invoking actor's `created` contribution keyed by
`envelope.execution` or, when that is absent, `EXT-op.<operationId>`.

#### VIG-PROV-010 — Lineage is not authorship
**Level:** MUST · **Release:** v1 · **Source:** RQ-ROS-2026-A008, DF-ROS-2026-A037 §4

The item's `derivedFrom` MUST be the received block's `derivedFrom` followed by
the request's source reference (for example `aegis:finding/SF-0001`, read from
the payload's `context.source.ref`; see ADR-0004). The
received `envelope.provenance` describes the payload's upstream origin (for
example the agent that discovered a finding). It MUST be kept verbatim as the
item's `receivedProvenance` — supported or unsupported — and MUST NOT be merged
into the item's own contributions, so an upstream author never becomes the
follow-up's author and the follow-up's creator never becomes the upstream
author.

#### VIG-PROV-011 — Replay is idempotent
**Level:** MUST · **Release:** v1 · **Source:** VIG-AGT-035, RQ-ROS-2026-A004

Replaying the same `operationId` MUST yield the same item (the item id is
derived deterministically from the operation id) and MUST NOT add a duplicate
record or contribution. Reusing an `operationId` for a different request MUST be
refused as `OperationAlreadyProcessed`.

#### VIG-PROV-012 — Vigila's own transformation
**Level:** MAY · **Release:** v1 · **Source:** DF-ROS-2026-A037, echelon-registry envelope v2 receiver rules

Vigila MAY record its own `transformed` contribution as the automation actor
`echelon/vigila` (`provider: echelon`, `model: unknown`, `runtime: vigila`) keyed
`EXT-vigila.<operationId>`, because it changes the request's representation into
an item. It MUST NOT record itself as `created` for a request another actor
invoked.

#### VIG-PROV-013 — No credentials in provenance
**Level:** MUST · **Release:** v1 · **Source:** RQ-ROS-2026-A017, VIG-SEC-005

A block or envelope actor containing a credential-like value anywhere MUST be
rejected as malformed. Vigila MUST NOT write authentication material into
provenance.

#### VIG-PROV-014 — Identity is not authority
**Level:** MUST · **Release:** v1 · **Source:** RQ-ROS-2026-A010, RQ-ROS-2026-A019

Recorded identity is self-reported provenance. Vigila MUST NOT grant, deny or
weight any operation, query or result by the recorded actor.

## Projection and conformance

#### VIG-PROV-015 — Documented legacy projection
**Level:** MUST · **Release:** v1 · **Source:** VIG-DOM-035, VIG-DOM-037, RQ-ROS-2026-A001

Where a `{type, name}` actor is still written (history entries, notes, the
item's `createdBy`), it MUST be the projection of the Praxis actor that
performed the operation:

| Praxis `kind` | `ActorType` | `name` |
|---|---|---|
| `agent` | `Agent` | the actor `id` |
| `human` | `Human` | the actor `id` |
| `automation` | `AutomatedProcess` | the actor `id` |
| `unknown`, any `x-…` | `Unknown` | the actor `id` (literally `unknown` when unknown) |

`Unknown` is added to `ActorType` for this purpose only; it is never produced
from a version 1 record. `createdVia` for an item received through
`followup.create` is `Integration`. The projection is lossy by design; the block
is the source of truth.

#### VIG-PROV-016 — History and notes point at their contribution
**Level:** SHOULD · **Release:** v1 · **Source:** RQ-ROS-2026-A004, VIG-DOM-033

A history entry or note written by an attributed operation SHOULD record the key
of the contribution it belongs to, so every history row can be traced to one
execution without duplicating the actor.

#### VIG-PROV-017 — Conformance against the shared fixtures
**Level:** MUST · **Release:** v1 · **Source:** RQ-ROS-2026-A018, DF-ROS-2026-A037 §7

Vigila's codec MUST reach the reference library's verdict and warning count on
every case in the vendored Praxis `cases.json`, and replaying the
`vigila:followup/FU-0001` record of `echelon-chain.json` MUST reproduce its
originator and roles. The fixtures are vendored unchanged with their source
commit and SHA-256, and a test MUST detect any local edit.

## Traceability

| Requirement | Implementation | Tests |
|---|---|---|
| `PROV-001`, `PROV-003`, `PROV-004`, `PROV-005`, `PROV-008` | `src/Vigila.Semantic/Provenance.fs` | `tests/Vigila.Semantic.Tests/ProvenanceTests.fs` |
| `PROV-002`, `PROV-015`, `PROV-016` | `src/Vigila.Semantic/Actors.fs`, `Item.fs`, `History.fs`, `Notes.fs`; `src/Vigila.Transition/Transitions.fs`; `src/Vigila.Application/Attribution.fs` | `tests/Vigila.Application.Tests/ProvenanceIntakeTests.fs` |
| `PROV-006`, `PROV-013`, `PROV-017` | `src/Vigila.Application/ProvenanceJson.fs` | `tests/Vigila.Application.Tests/ProvenanceConformanceTests.fs` |
| `PROV-007`, `PROV-008` | `src/Vigila.Host.GitHub/ItemJson.fs` | `tests/Vigila.Host.GitHub.Tests/ProvenancePersistenceTests.fs` |
| `PROV-009`..`PROV-012`, `PROV-014` | `src/Vigila.Application/FollowUpIntake.fs` | `tests/Vigila.Application.Tests/ProvenanceIntakeTests.fs` |
