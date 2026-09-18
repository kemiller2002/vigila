---
id: REQ-AGT
title: Agent and integration interface
status: draft
sources: v0.2 §22–§25, §30–§33, v0.3 §64–§68, §74, §75, v0.4 §114, §115, §126–§130, §138–§140
---

# Agent and integration interface

Agent interaction is a primary requirement, not an afterthought (v0.2 §22). The
recurring theme across all four documents is that agents get an explicit,
deterministic, narrowly scoped API — never raw repository access, and never
probabilistic behaviour inside the domain.

## Operations

#### VIG-AGT-001 — Agents use defined operations
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §22, v0.1 §22

Agents MUST interact with Vigila through defined operations rather than
modifying persistence directly.

#### VIG-AGT-002 — No unrestricted repository write access
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §22, v0.1 §22

Agents MUST NOT be given unrestricted repository write access as the normal
application API.

#### VIG-AGT-003 — Initial operation set
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §22, v0.1 §22

The initial operation set MUST include:

| Read | Write | Lifecycle |
|---|---|---|
| `GetFollowUp` | `AddFollowUp` | `CompleteFollowUp` |
| `SearchFollowUps` | `UpdateTitle` | `CancelFollowUp` |
| `GetDueFollowUps` | `UpdateDescription` | `ReopenFollowUp` |
| `GetWaitingFollowUps` | `AddNote` | `DeferFollowUp` |
| | `SetNextAction` | `SetStatus` |
| | `SetDueDate` | |
| | `SetFollowUpDate` | |
| | `SetWaitingOn` | |
| | `SetKind` | |
| | `AddTag` | |
| | `RemoveTag` | |

Plus the operations added by later documents: `GetCapabilities`
([VIG-AGT-040](#vig-agt-040)), `GetStatus` ([VIG-AGT-041](#vig-agt-041)), and
`ValidateWorkspace` ([VIG-PER-050](PERSISTENCE.md#vig-per-050)).

## Natural-language capture

#### VIG-AGT-004 — Speech translates to explicit commands
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §23, v0.1 §23

The architecture MUST make it easy for conversational agents to translate normal
speech into explicit Vigila operations. Worked examples from §23:

| The user says | The agent issues |
|---|---|
| "Remind me Friday to check whether John sent the contract." | `AddFollowUp` + `SetFollowUpDate` |
| "I'm waiting on the accountant about the generator. Bring it back next Tuesday." | `SetWaitingOn` + `SetStatus(Waiting)` + `SetFollowUpDate` |
| "Add a note that she called today and said it will probably be ready next week." | `AddNote` |
| "Mark the restaurant submission follow-up complete." | `SearchFollowUps` + `CompleteFollowUp` |
| "Show me everything I'm waiting on related to travel." | `SearchFollowUps` filtered by status and tag |

#### VIG-AGT-005 — Vigila contains no LLM
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §23, v0.3 §58

Vigila itself MUST NOT require an LLM to function.

## Ambiguity and uncertainty

#### VIG-AGT-010 — Search before creating
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §24, v0.1 §24

Agents SHOULD search before creating likely duplicate items. When the user
references an existing item ambiguously, the agent SHOULD search existing active
items first.

#### VIG-AGT-011 — Expose ambiguity, do not guess
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §24, v0.4 §115

Where one clear match exists the agent MAY update it. Where multiple materially
plausible matches exist, the calling agent MUST expose the ambiguity. The core
Vigila API MUST NOT perform probabilistic guessing.

#### VIG-AGT-011a — Uncertainty is not domain state
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §115

Search and match operations MAY return ambiguity or uncertainty information, but
low confidence in an agent match MUST NOT silently alter permanent item state.
An agent MUST NOT persist a confidence score unless a defined feature explicitly
requires it. The core domain MUST NOT manufacture confidence scores.

## Idempotency and receipts

#### VIG-AGT-012 — Caller-supplied OperationId
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §65, §97

Mutation requests MUST support a caller-supplied `OperationId`, unique within an
appropriate scope. Repeating the same `OperationId` MUST NOT repeat the
mutation. Where practical the system SHOULD return the original successful
result when a completed `OperationId` is retried.

> The problem §65 names: an agent submits a mutation, GitHub persists it, and
> the response is lost. The retry must not create a duplicate effect.

#### VIG-AGT-013 — Idempotency scope
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §65

Idempotency rules MUST apply especially to item creation, note creation,
completion, tag addition and removal, and date changes. Idempotency records MUST
NOT expose credentials or sensitive transport details.

#### VIG-AGT-014 — Mutation receipts
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §66, §97

Every successful mutation MUST return a deterministic receipt containing at
least: success/failure state, `ItemId` where applicable, resulting item version,
`OperationId`, timestamp, and error information where unsuccessful.

```json
{
  "status": "success",
  "itemId": "VIG-000123",
  "version": 7,
  "operationId": "...",
  "updatedAt": "..."
}
```

#### VIG-AGT-015 — Receipts never claim premature success
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §66

Agents MUST be able to distinguish accepted and persisted changes from attempted
changes. A result MUST NOT claim success until persistence has succeeded. A
receipt MUST be safe to log and MUST never include a GitHub token.

## Narrow mutation semantics

#### VIG-AGT-020 — Commands do only what they say
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §67, §97

Each command MUST modify only the state it owns or explicitly declares.

> §67's example: "Add a note saying John called" MUST NOT also change the due
> date, mark the item Waiting, alter tags, rewrite the title, or complete the
> item.

#### VIG-AGT-021 — Multi-field changes are explicit
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §67

Multi-field changes MUST require an explicit composite command
([VIG-AGT-024](#vig-agt-024)) or multiple explicit operations. Side effects MUST
be documented. Agents MUST NOT infer unrelated domain transitions from a narrow
mutation request.

#### VIG-AGT-022 — Deterministic implication only
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §105

Agents MUST set optional closure metadata only when explicitly requested or
deterministically implied.

#### VIG-AGT-023 — NeedsReview
**Level:** MAY · **Release:** v1 · **Source:** v0.4 §114

An item MAY be marked `NeedsReview`. It MUST be metadata rather than a new
workflow state. Agents MAY set it when instructed or when calling policy
determines review is required. It MUST be visible and filterable, and human
review MUST be able to clear it explicitly.

#### VIG-AGT-024 — Atomic composite mutations
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §127

Vigila MUST support an explicit composite mutation mechanism where atomic
behaviour is needed — for example add note + set Waiting + set `WaitingOn` + set
follow-up date as one operation.

- Composite mutations MUST declare all intended changes.
- Either all changes succeed or none are treated as committed, where technically
  feasible.
- Composite history MUST remain understandable.
- Composite operations MUST honour `ExpectedVersion` and `OperationId`.
- Composite operations MUST NOT become a loophole for unrestricted arbitrary
  mutation.

## Concurrency

#### VIG-AGT-030 — Optimistic concurrency
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §68, v0.2 §29, §97

Optimistic concurrency MUST be explicit. Every mutable item MUST carry a version
value or equivalent concurrency token. Mutation requests MUST supply the
expected current version. A mismatch MUST return a conflict rather than
overwriting newer state.

```
Update VIG-42, ExpectedVersion = 17
Current version = 18  →  Conflict
```

#### VIG-AGT-031 — Conflicts are recoverable
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §68

A conflict response MUST provide enough information for the caller to re-read
and retry safely. Agents MUST NOT automatically overwrite conflicts without
first reading the newer state.

#### VIG-AGT-032 — Stable concurrency concept
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §68

The implementation MAY use Git object identifiers, record versions, or another
deterministic mechanism, but the domain and integration contract MUST expose a
stable concurrency concept independent of that choice.

#### VIG-AGT-033 — Read-before-write
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §126, §141

Agent mutations against an existing item MUST include `ExpectedVersion`. A caller
lacking current version information MUST retrieve the item first. The
integration contract MUST make read-before-write expectations explicit. Blind
overwrite operations MUST NOT be part of the normal public API.

#### VIG-AGT-034 — Concurrency assumptions
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §29, v0.1 §29

The system MUST assume the user may have Vigila open in multiple browser
sessions, that multiple agents may interact with it, and that automated
integrations may modify items. An outdated client MUST NOT silently overwrite
newer changes.

## Command envelope

#### VIG-AGT-035 — Common command envelope
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §128, §141

All mutating integration commands MUST share a standard envelope containing:
`OperationId`, `Actor`, `CreatedVia`, `ExpectedVersion` where applicable,
request timestamp/context where needed, and the command payload.

Command metadata MUST be consistent across operations. The envelope MUST never
include the GitHub token. `Actor` and `CreatedVia` semantics MUST be documented,
and `OperationId` idempotency behaviour MUST be uniform.

## Errors

#### VIG-AGT-050 — Machine-readable error taxonomy
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §129, §141

Errors MUST use stable machine-readable codes from at least this set:

| Code | Code | Code |
|---|---|---|
| `NotFound` | `SchemaUnsupported` | `RateLimited` |
| `Conflict` | `ValidationFailed` | `StorageCorrupt` |
| `InvalidTransition` | `RepositoryUnavailable` | `OperationAlreadyProcessed` |
| `Unauthorized` | `RepositoryNotFound` | `PersistenceFailed` |
| `Forbidden` | `BranchUnavailable` | |

Agents MUST NOT need to parse human error text. Each error MAY include a
human-readable message in addition to the code. Error codes MUST be stable
across versions, and sensitive values MUST NOT appear in errors.

#### VIG-AGT-051 — Retryability marker
**Level:** SHOULD · **Release:** v1 · **Source:** v0.4 §130, §141

Error responses SHOULD include a retryability classification where meaningful.
`RateLimited` and `RepositoryUnavailable` MAY be retryable; `InvalidTransition`
and `ValidationFailed` are not retryable without a request or state change.
Retry guidance MUST NOT encourage blind retries on conflicts. Rate-limit
responses MAY include retry-after information when safely available.

## Discovery and health

#### VIG-AGT-040 — Capability discovery
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §75, §97

The integration contract MUST provide a `GetCapabilities` operation reporting
API/schema version, supported commands, supported item kinds, supported states,
supported optional features, and maximum supported payload sizes where relevant.

Clients MUST NOT have to assume they are talking to the newest Vigila version.
Capability discovery MUST be deterministic, and unsupported commands MUST fail
explicitly rather than being ignored.

#### VIG-AGT-041 — Health and status
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §76, §97

Vigila MUST expose a `GetStatus` operation reporting: Vigila available,
repository connected, schema supported, read available, write available, branch
valid, storage path initialised.

Status checks MUST NOT mutate data and MUST NOT expose the GitHub token. The UI
and agents MUST be able to distinguish application available, repository
unavailable, authentication failure, schema incompatibility, read-only access,
and write-capability failure.

## Integration contract

#### VIG-AGT-042 — Integration assembly
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §30, v0.1 §30

Vigila SHOULD publish an integration assembly/package defining identifiers,
supported request types, response types, events where appropriate,
serialisation contracts, schema versions, and validation rules.

#### VIG-AGT-043 — No reverse-engineering of persisted JSON
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §30, v0.1 §30

Consumers MUST NOT reverse-engineer Vigila's internal persisted JSON.

#### VIG-AGT-044 — Integration boundaries
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §31, v0.1 §31

Other applications (Chrona, Summa, ROS, agents) MUST communicate through
Vigila's supported integration contract and MUST NOT write directly to Vigila's
repository structures.

#### VIG-AGT-045 — Cross-application references
**Level:** SHOULD · **Release:** v1 · **Source:** v0.4 §140

A cross-application reference SHOULD support application/system type, stable
record ID, optional current URL, optional display name, and optional integration
version. URLs MUST be treated as navigation hints rather than primary identity,
references SHOULD survive deployment URL changes where possible, and Vigila MUST
NOT need to understand another application's internal storage.

> Extensive cross-application reference resolution is deferred by §142.

## Domain events

#### VIG-AGT-046 — Domain events
**Level:** MAY · **Release:** v1 · **Source:** v0.2 §33, v0.1 §33

Useful domain events MAY include `FollowUpCreated`, `FollowUpCompleted`,
`FollowUpReopened`, `FollowUpBecameDue`, `FollowUpReachedFollowUpDate`,
`FollowUpDeferred`, `FollowUpSetWaiting`, `NoteAdded`, `TagAdded`, `TagRemoved`.

The initial application MUST NOT require a complex event-driven architecture.
Domain events SHOULD exist only where they provide useful integration semantics.

## ROS integration

#### VIG-AGT-060 — Vigila and ROS stay separate
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §32, v0.1 §32

Vigila and ROS MUST remain separate systems. A Vigila item MAY reference a ROS
work item.

#### VIG-AGT-061 — Promote-to-ROS contract
**Level:** MUST · **Release:** future · **Source:** v0.4 §138, v0.2 §32

Promotion MUST record: Vigila `ItemId`, ROS `WorkItemId`, promotion timestamp,
actor, `OperationId`.

- Repeating the same promotion operation MUST NOT create multiple ROS work
  items.
- Promotion MUST add a durable cross-reference in Vigila.
- Promotion MUST NOT erase Vigila notes or history.
- The resulting Vigila state/resolution MUST follow an explicit policy.
- A failed ROS creation MUST NOT leave Vigila falsely marked as promoted.

#### VIG-AGT-062 — ROS state does not implicitly control Vigila
**Level:** MUST · **Release:** future · **Source:** v0.4 §139

Completing a ROS work item MUST NOT automatically complete the Vigila item
unless an explicit integration rule has been configured. Cancelling a Vigila
item MUST NOT automatically cancel ROS work. Cross-system synchronisation rules
MUST be explicit and versioned, and default behaviour MUST preserve system
independence.

## Bulk operations

#### VIG-AGT-070 — Batch operations
**Level:** MAY · **Release:** deferred · **Source:** v0.3 §64, §98

Vigila MAY eventually allow batch complete, defer, snooze, add tag, remove tag,
change follow-up date, and cancel. If implemented:

- Batch operations MUST apply the same domain validation as individual
  operations.
- Partial failure MUST be reported explicitly; batches MUST NOT silently skip
  failed items.
- Each affected item MUST retain its own history and audit trail.
- Where practical a batch SHOULD share a parent operation identifier.

> Classified in §64 as v1.1 / future upgrade.
