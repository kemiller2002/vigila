---
id: ADR-0004
title: Vigila adopts the Praxis actor and provenance record, with an explicit, lossless mapping
status: accepted
created: 2026-09-26
work_item: WI-0029
requirements: [VIG-DOM-050, VIG-DOM-051, VIG-DOM-052, VIG-DOM-053, VIG-DOM-054, VIG-DOM-055, VIG-PER-024, VIG-PER-025, VIG-AGT-036, VIG-AGT-037]
external_contract: [RQ-ROS-2026-A001, RQ-ROS-2026-A004, RQ-ROS-2026-A013, RQ-ROS-2026-A014, RQ-ROS-2026-A015, DF-ROS-2026-A037]
---

# ADR-0004 — Praxis provenance in Vigila

## Context

Before this decision, a Vigila actor was only `{Type; Name}`: free text and
four types. A history record named the actor but not the run that acted. The
item file reader kept only `type` and `name` and dropped every other actor
field. Several different identities touch one follow-up:

- the agent that discovered the finding;
- the system that generated the follow-up;
- the agents that later handled it;
- the human that validated it.

Vigila could not tell these apart. It could not keep them apart across a
rewrite either.

Praxis owns the Echelon provenance contract:

- the actor (`RQ-ROS-2026-A001`);
- execution-keyed contributions (`RQ-ROS-2026-A004`);
- the versioned interchange record `praxis.provenance-record`
  (`RQ-ROS-2026-A013`–`A015`, `DF-ROS-2026-A037`).

Vigila must adopt that contract without depending on Praxis.

## Decision

### 1. The actor is the Praxis actor

`Actor` gains two fields:

- `Tooling: {Provider; Model; Runtime} option`, which is `None` exactly for a
  human;
- `Extensions`, which holds the fields Vigila does not model, in the order it
  read them.

A non-human actor with nothing declared has `Tooling` of `"unknown"` three
times. Nothing is guessed. The actor's `Name` is its Praxis `id`.

### 2. Type to kind is explicit, and lossless

| Vigila `ActorType` | Praxis `kind` | Kept distinguishable by |
|---|---|---|
| `Human` | `human` | the kind |
| `Agent` | `agent` | the kind |
| `AutomatedProcess` | `automation` | the kind, with no Vigila type field |
| `Integration` | `automation` | `type: "integration"` in the item file; `x-vigila-type: "integration"` in a provenance record |

`Integration` is a deterministic, non-agent process, so its Praxis kind is
`automation`. Two alternatives were rejected:

- **`x-integration`.** It is a legal extension kind, but every other Echelon
  consumer would read it as a category it does not know. It would no longer
  read the actor as automation.
- **Plain `automation`.** It would round-trip back as `AutomatedProcess`.

The Vigila type therefore rides beside the kind:

- **Item file.** `type` is Vigila's own schema field.
- **Interchange record.** The field is the namespaced `x-vigila-type`. It
  cannot collide with a field a later contract version defines. Every
  conforming consumer preserves it, because unknown actor fields are preserved.

The reverse mapping yields no Vigila type for `unknown` and `x-...` kinds.
Those actors stay in the record and are never coerced.

### 3. The item file (schema version 2)

A persisted actor is written as follows:

```json
{"type":"agent","name":"openai/codex","kind":"agent","id":"openai/codex",
 "provider":"openai","model":"gpt-5-codex","runtime":"codex", ...extensions}
```

The object is therefore itself a conforming Praxis actor. A human omits
`provider`, `model` and `runtime`. The reader enforces three things:

- a `kind` or `id` that contradicts `type` or `name` is refused;
- a human that claims tooling is refused;
- unknown actor fields are kept.

A history record gains `"execution": "EXE-…" | "CTB-…"` when the run is known,
and omits it otherwise. The `contributed` operation carries `operations`,
`reason` and `evidence`. The item gains `provenance`: the interchange record,
written exactly as held, or `null`.

`schemaVersion` goes from 1 to 2 although every field is additive. A version 1
reader ignores unknown fields (`VIG-PER-022`). It would load a version 2 item
and silently strip its provenance on its next write, which is exactly what
`RQ-ROS-2026-A015` forbids. Raising the version makes that reader refuse the
record instead (`VIG-PER-014`).

Version 1 items still load, and nothing is invented for them:

- no execution;
- no provenance;
- a non-human actor's tooling is `"unknown"`;
- nothing is inferred from Git, from `CreatedVia`, or from the free-text
  name.

### 4. Executions

- **Propagated execution.** An operation is keyed by the propagated
  `ROS_EXECUTION_ID` when the caller has one.
- **Vigila's own run.** Otherwise it is keyed by the caller's own run,
  `EXE-<system>.<run>`. For Vigila itself this is `EXE-vigila.<run>`, from
  `ContributionKey.foreignExecution`.
- **Outside any run.** A human or automation outside any run uses `CTB-…`.

An agent is never keyed by `CTB-…`. Vigila never mints a Praxis-shaped
`EXE-<timestamp>-<random>`.

A UI capture remains attributed to the human `local`, with no execution. It
really is a human action at that browser. Making it configurable would have
needed an identity source the browser does not have, so it stays explicit and
documented in `Dispatch.fs`.

### 5. The interchange codec lives in Tier 1

`Vigila.Semantic.Provenance` is FSharp.Core plus the BCL, with no serializer.
It contains:

- the typed view;
- validation, including the credential guard;
- `append`, `derive` and `successorProblems`;
- `chain`.

It works on `Vigila.Semantic.Carried.Verbatim`, a minimal JSON-shaped value.
Object members keep their order and numbers keep their source text, so the
record is edited in place and never re-rendered from a model.

Tier 4 (`Vigila.Host.GitHub.ProvenanceJson`) only converts between `Verbatim`
and JSON text. It refuses duplicate member names rather than collapsing them.

The codec is placed in Tier 1 because `Item.generate` and `Item.contribute`,
which are Tier 1 item operations, must decide whether a contribution is legal.
The tier check still holds: no package reference and no `System.Text.Json`.

### 6. Roles stay separate

- **`Item.generate`.** The generating system becomes the creator. It is the
  record's only `created` contribution, and it appears in `CreatedBy` and in the
  first history record, with its run. Each source is named in `derivedFrom`, and
  its record is carried verbatim in `sources`. The discovering agent therefore
  appears only in that snapshot.
- **`Item.contribute`.** Each handler, resolver or validator gets a history
  record with its own actor and execution. The same contribution is appended to
  the record under the Praxis rules. A refusal leaves the item unchanged. A
  record in an unsupported major version is carried unchanged and not extended.

### 7. Conformance

Tests run against the Praxis fixtures, which are vendored under
`tests/fixtures/praxis-provenance-record/`. Their `SOURCE.json` records the
repository, the commit (`b32677d`) and a SHA-256 for every file. The tests
cover:

- every case;
- every successor pair;
- every end-to-end hop;
- the reconstructed chain.

They also drive e2e/06 → 07 → 08 → 09 through `Item.contribute` and the item
file format.

## Consequences

- A record's `subject` is the record's own statement. Vigila writes
  `vigila:item/<ItemId>` for records it starts, and does not rewrite a subject
  it received.
- Ordinary edits do not append to the interchange record. Tag, note, title and
  status changes stay in Vigila's own history. Only explicit contributions do.
  An item with no record gains none from a contribution: starting one would
  require an origin Vigila does not have.
- The agent command envelope (`VIG-AGT-035`, `VIG-AGT-036`) is still not
  implemented. When it is, it supplies the `Attribution` these operations
  take.
- Identity remains self-reported provenance. It is not authentication,
  authorization, or evidence.

## Revisit when

- The agent API and command envelope are built (`VIG-AGT-035`).
- Praxis publishes a second major version of the interchange record.
- Praxis adds an attestation authority. Its signed fields ride in
  `Extensions` and in the verbatim record, which are both preserved already.
