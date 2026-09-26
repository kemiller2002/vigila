---
id: ADR-0004
title: The followup.create provider boundary and durable idempotency
status: accepted
created: 2026-09-26
work_item: VIG-15
amends: ADR-0002
---

# ADR-0004 — The `followup.create` provider boundary and durable idempotency

## Context

Issue #15 makes Vigila the provider of the Echelon `followup.create` v1
capability defined in `kemiller2002/echelon-registry`
(`contracts/followup-create.v1.schema.json`,
`schemas/execution-envelope.schema.json`, `spec/followup-protocol.md`). The
registry's invariant is that every Echelon component stays independently
installable and functional; optional integrations add capability and their
absence never breaks core behaviour.

`VIG-AGT-012` requires that repeating an `OperationId` never repeats a
mutation, including when the response was lost after persistence succeeded.
That rules out an in-memory operation index: a restarted process, a second
browser session or a second agent would not see it. `VIG-AGT-034` requires
concurrent agents to be assumed, so a find-then-record check is also
insufficient -- two simultaneous calls can both find nothing and both create.

## Decision

### D1 — Vigila owns the boundary; nothing consults a registry

`Vigila.Application.Integration` accepts the semantic request and envelope and
translates them into the existing Item aggregate (`Kind = FollowUp`,
`CreatedVia = Integration`). No parallel follow-up domain is introduced.
`Vigila.Application.IntegrationWire` decodes the JSON invocation, negotiates
the capability and contract version, and encodes receipts. Neither references
a registry, resolver, or any other Echelon system; registry discovery belongs
to consumers.

The field mapping is documented at the head of `Integration.fs`. Values
Vigila's Item cannot represent -- provider, run and session ids, correlation
id, exact priority, the opaque `context` -- are kept in the operation record
(D3) rather than squeezed into free text such as `Actor.Name`.

### D2 — Idempotency is a create-only claim, not a lookup

The application port `FollowUpLedger` has one member, `Record`, whose contract
is an atomic claim of the operation id: exactly one concurrent caller observes
`Recorded`; every other observes `AlreadyRecorded` with the winner's record.
There is deliberately no separate find method to race against.

The Tier 4 implementation (`Vigila.Host.GitHub.FollowUpLedger`) realises the
claim as a create-only write of the operation record. GitHub's contents API
refuses a PUT without a blob SHA when the file exists, so the repository
itself arbitrates. The ledger holds no state, so a restart changes nothing.

A replay whose normalised request differs from the recorded one is a
`Conflict`, not a silent success.

### D3 — Layout: one operation record per operation, beside `items/`

```
<storagePath>/workspaces/<workspaceId>/
  items/<itemId>.json
  operations/<sha256(operationId)>.json
```

The file is named by the SHA-256 of the operation id because the id is
caller-supplied text and cannot safely be a filename; the id itself is stored
inside the record. Idempotency is scoped to the workspace.

**Relation to ADR-0002 D4 ("no index").** An operation record is not an index,
cache, or database in `VIG-UI-026`'s sense: it does not duplicate item state
to make reads cheaper, and nothing lists or queries it. It is the durable fact
`VIG-AGT-012` (MUST) requires -- "this operation happened, and produced this
item" -- and has no other home. ADR-0002's one-file-per-item rule and blob-SHA
concurrency token are unchanged.

### D4 — Write order and recovery

The operation record is written first, then the item file, both create-only.
If the item write fails, the invocation reports `Failed` (never success,
`VIG-AGT-015`), and any replay writes the item from the record. Because the
item write is create-only, a repair can never overwrite an item edited since
creation. A single-commit write of both files through the Git data API would
remove the window entirely and can replace this without changing the port.

### D5 — Operational faults go through Aegis

Typed ledger failures and unexpected exceptions both become Aegis faults
(`VIGILA.INTEGRATION.*`). Validation refusals, unsupported versions and
conflicts are typed domain outcomes, not faults, as the registry's "Shared
Echelon capability boundaries" section requires.

## Consequences

- Durable, concurrency-safe idempotency holds for any `RepositoryFiles`
  implementation whose `CreateNew` is atomic. It is proven in tests against a
  store with GitHub's create-only semantics, including across ledger instances
  (restart) and under 64 simultaneous invocations.
- **Not yet proven against live GitHub.** `GitHubStore` remains a scaffold
  that performs no GitHub call (pre-existing; see `aegis-boundaries.json`,
  "Future direct GitHub host"). The GitHub-backed `RepositoryFiles` is the
  remaining step, and belongs to the persistence work that implements that
  host.
- No transport invokes the boundary yet (no CLI or HTTP entry point). That is
  a separate delivery decision; the boundary is complete and testable without
  it.
