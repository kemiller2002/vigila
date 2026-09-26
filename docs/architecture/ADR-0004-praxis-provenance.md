---
id: ADR-0004
title: Vigila carries Praxis provenance instead of its own identity model
status: accepted
created: 2026-09-26
work_item: FEAT-ECHELON-PROVENANCE
requirements: [VIG-PROV-001, VIG-PROV-002, VIG-PROV-006, VIG-PROV-007, VIG-PROV-009, VIG-PROV-010, VIG-PROV-011]
provenance:
  contributions:
    EXE-20260926T081409758Z-615c839a:
      operations: [created]
      at: 2026-09-26T08:34:28.669Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Record the Praxis provenance design for Vigila (FEAT-ECHELON-PROVENANCE)"
    EXE-20260926T085500134Z-ac7e0976:
      operations: [modified]
      at: 2026-09-26T09:02:59.334Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Adopt Praxis provenance contract revision 1.1 and registry REG-PROV-008 v1 keys (FEAT-ECHELON-PROVENANCE-R1)"
---

# ADR-0004 — Praxis provenance in Vigila

## Context

Vigila items record one `Actor {type, name}` for their creator and one per
history entry and note. Follow-ups now arrive from other Echelon systems
(`followup.create`) and are handled by several agents and people, and the owner
requires Vigila to keep apart the agent that discovered something, the system
that generated the follow-up, the agents that later handled it, the human who
reviewed or resolved it, and the execution behind each. Praxis already defines
that model and its interchange block (`DF-ROS-2026-A036`, `DF-ROS-2026-A037`,
kemiller2002/praxis@a42c44e, revised to contract 1.1 at c2657ef); a Vigila-specific model would be the schema fork
`DF-ROS-2026-A037` rejects.

## Decision

1. **Typed block in Tier 1, codec in Tier 3.** `Vigila.Semantic.Provenance`
   holds `praxis.provenance/1` as typed data and implements the append rules
   (the reference library's `appendContribution`, `addLineage`, `originator`,
   `withRole`). Fields the version does not model travel as opaque JSON text in
   `Extensions`, because Tier 1 may not reference `System.Text.Json`.
   `Vigila.Application.ProvenanceJson` classifies JSON into
   supported/unsupported/malformed and writes blocks back; it is tested against
   every vendored Praxis conformance case.
2. **Two slots per item.** `Item.Provenance` is the item's own block: who
   created, handled, resolved and reviewed *this follow-up*. `Item.ReceivedProvenance`
   is `envelope.provenance` exactly as received — the upstream record's origin,
   such as the agent that discovered a finding. The received block is never
   merged into the item's own contributions. Reason: an upstream block already
   has its own `created` (the finding's discoverer), and appending the
   follow-up's creator to it would either be refused by the contract (second
   `created`) or, worse, present the discoverer as the follow-up's author. The
   envelope receiver rule "preserve and append the invoking actor" is met by
   preserving the received block verbatim and appending the invoking actor's
   contribution to the new record's block, whose lineage (`derivedFrom`) points
   back at the source.
3. **Legacy fields are a projection.** `createdBy`, history `actor` and note
   `createdBy` stay, projected from the Praxis actor by the table in
   `VIG-PROV-015`. `ActorType.Unknown` is added only for Praxis `unknown` and
   `x-...` kinds. History entries and notes gain an optional `contribution` key
   instead of a second copy of the actor.
4. **Schema version chosen by content.** A record is written as
   `schemaVersion: 2` only when it holds provenance, a contribution link or an
   `unknown` actor; everything else is written as version 1, unchanged, so
   older builds keep reading every record this change did not touch. There is
   no migration (`VIG-PER-023`); legacy records are never rewritten or
   backfilled.
5. **Deterministic intake.** `FollowUpIntake.receive` takes its time from the
   envelope and derives the item id from the operation id (a name-based
   version-8 GUID over SHA-256), so replaying a request builds the same item; a
   stored item is returned unchanged after confirming that re-appending its
   contributions changes nothing.
6. **Envelopes are held to the registry schemas.** Earlier Vigila ignored
   unknown envelope properties. It now rejects any property outside
   `echelon.execution-envelope/v1` or `/v2`, allowing only `x-...` extension
   properties on v2, and checks `source` and the v1 actor's `knownValue`
   shapes. Reason: the registry schema says so (`additionalProperties: false`),
   and a misspelt `execution` or `provenance` silently ignored would drop
   identity. v1 keys follow REG-PROV-008 (`EXT-run.<repository>.<runId>` when
   the repository is known) with the Praxis 1.1 injective escaping.
7. **Source reference convention.** `followup.create` v1 leaves `context` open.
   Vigila reads `context.source` as `{ref, url?, displayName?}`, where `ref` is
   a namespaced record reference (`aegis:finding/SF-0001`). It becomes both a
   `SourceReference` and a `derivedFrom` entry. Other `context` fields are not
   stored.

## Consequences

- A build before this change refuses version 2 records instead of misreading
  them, and still reads every version 1 record.
- Status changes gained a pure `Transitions.changeStatus`; attributed
  operations (`Attribution.changeStatus/resolve/addNote/review/contribute`)
  append a contribution and return an item only when both the change and the
  contribution succeed.
- The UI's local capture path still records `Actor.human "local"` without
  provenance: it has no declared execution, and inventing one is forbidden.
- Nothing grants, denies or weights anything by actor (`VIG-PROV-014`).

## Revisit when

- Praxis publishes a shared .NET codec (replace `ProvenanceJson` and the typed
  model with it).
- A second interchange major version is defined.
- The Echelon registry publishes `echelon.execution-envelope/v2` or a
  `followup.create` v2 with a defined source-reference field.
