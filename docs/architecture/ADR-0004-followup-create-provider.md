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
conflicts are typed domain outcomes, not faults. The GitHub transport returns
only typed failures and never response bodies, URLs containing credentials, or
credentials themselves.

### D6 — The receiving Vigila installation owns the remote transport

`GitHubRepositoryFiles` is the production `RepositoryFiles` adapter. It
uses GitHub's REST contents API directly against a configured repository and
branch. It never clones the receiving repository and requires no destination
working tree.

`CreateNew` issues the create-file PUT first and deliberately omits an
existing blob SHA. GitHub therefore arbitrates create-if-absent. Only after a
409/422 refusal may the adapter read the destination to distinguish an
already-existing file from another branch/write refusal. This preserves the
atomic claim required by D2.

A 404 read is not blindly treated as absence: because GitHub also uses 404 for
an invisible repository, the adapter verifies repository visibility before
returning `None`. Authentication, authorization, rate-limit and transport
failures remain typed operational failures.

Authentication is a Tier 4/composition concern. The adapter accepts a
credential provider; the executable resolves an environment-supplied token.
A GitHub Actions token, GitHub App installation token, fine-grained token, or
future provider can therefore be used without changing the application
boundary. Tokens are never persisted in operation records, passed into
semantic/application records, or emitted in machine results.

The minimum receiving-repository permission is **Contents: read and write**
(with GitHub's metadata read access). The configured branch must permit the
installation/token to create files.

### D7 — `vigila follow-up add` is the receiving-system integration boundary

The `Vigila.Cli` executable is the anti-corruption/composition layer owned by
the receiving Vigila installation. It accepts canonical integration JSON from
`--input FILE` or `--stdin`, constructs the GitHub adapter and ledger, and
invokes `IntegrationWire.invoke`. It does not duplicate contract parsing or
semantic validation.

Configuration is runtime data: `--repository OWNER/REPO`, `--branch`,
`--workspace`, `--storage-root`, and optional `--token-env NAME`.
Credential lookup defaults to `VIGILA_GITHUB_TOKEN`, then `GITHUB_TOKEN`,
then `GH_TOKEN`. No owner or organization is compiled into the executable.

Machine receipts distinguish `created`, `existing`, `conflict`,
`rejected`, and `failed`. Exit codes are stable: 0 created/idempotent
existing, 2 rejected/invalid input, 3 conflict, and 4 operational failure.

Registry discovery is intentionally absent. A caller may discover a Vigila
installation however it chooses, but the receiving executable never contacts
`echelon-registry`.

### D8 — Failure classification, bounded retry and typed exit codes (2026-10-05)

Amends D6/D7 after the Echelon quality inventory (VIG-F2, VIG-F3, VIG-F6;
work item WI-0029, building on #25).

- **Explicit status classes.** `GitHubStore.classifyStatus` is a pure, total
  function. 429 (or 403 with an exhausted rate limit) is `RateLimited`. 401,
  403, 404 and 409/422 keep their specific cases. 408 and every 5xx are
  `RepositoryUnavailable`, which is retryable. Every other 4xx is
  `RequestRejected status`, and any other status is `UnexpectedStatus
  status`; both are terminal. Before this change, every unlisted status
  (400, 410, …) fell into the retryable `RepositoryUnavailable`, so a
  permanent client error invited endless retries.
- **Bounded retry.** `RetryingRepositoryFiles.wrap` retries only retryable
  failures, under a `RetryPolicy` (standard: 3 attempts, 500 ms doubling,
  capped at 10 s, honouring `Retry-After`). When the budget runs out, the
  result is `RetriesExhausted(attempts, last)`, which is terminal. Retrying
  `CreateNew` is safe because it is create-only: a retried claim that had in
  fact succeeded observes `FileAlreadyExists` and is reported as `existing`.
  The decision (`decide`) is pure, and waiting is injected.
- **Receipt retryability is typed.** `CreateOutcome.Failed` carries a
  `FailureDisposition` (`RetrySafe` | `Terminal`). The receipt's `retryable`
  is derived from it, and is no longer always `true`. Replay with the same
  operation id stays *safe*; `retryable` now states whether it is *useful*.
  The receipt `code` remains `PersistenceFailed`. The new ledger codes
  (`RequestRejected`, `UnexpectedStatus`, `RetriesExhausted`) surface in the
  Aegis fault code (`VIGILA.INTEGRATION.<CODE>`).
- **Exit codes from the typed outcome.** `IntegrationWire.execute` returns the
  typed `CreateOutcome`. The CLI maps it with the exhaustive
  `Program.exitCodeOf` and encodes the receipt separately. It no longer
  re-parses its own JSON. The exit-code values above are unchanged.
- **Injected identity.** Tier 1 no longer calls `Guid.NewGuid`. `ItemId`,
  `NoteId` and `WorkspaceId` are drawn from an injected `IdSource` port, the
  same way time comes from `Clock`. The composition roots (`Vigila.Cli`,
  `Dispatch`) supply random GUIDs. Persisted forms (GUID `D`/`N`, `VIG-xxxxxxxx`
  display) are unchanged.
- **Ratchet.** `scripts/check-architecture.sh` now fails on ambient
  nondeterminism (`Guid.NewGuid`, `DateTime*.Now`, `Random`, …) in Tier 1/2,
  and on a host reading a receipt `status` back out of JSON.

## Consequences

- Durable, concurrency-safe idempotency holds for any `RepositoryFiles`
  implementation whose `CreateNew` is atomic. The existing 64-way test
  remains the concurrency proof at the ledger boundary.
- Controlled HTTP tests exercise existing/missing reads, auth/authorization
  and transient failures, successful create, already-existing create,
  repository/branch configuration, and credential containment without live
  GitHub credentials.
- A controlled end-to-end CLI test proves canonical JSON flows through
  `IntegrationWire.invoke`, `FollowUpLedger`, and the GitHub
  `RepositoryFiles` adapter to both the operation claim and item write.
- A live GitHub smoke test is optional. The architecture does not depend on
  one for normal test execution.
- Independently installed callers such as Praxis need only a configured Vigila
  executable and a credential for the receiving Vigila repository. They do not
  need the Vigila source repository or `echelon-registry` at invocation time.
