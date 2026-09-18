---
id: REQ-DOM
title: Domain model — items, kinds, states, fields
status: draft
sources: v0.2 §1–§15, v0.3 §85, v0.4 §100, §102, §104–§107, §109, §110
---

# Domain model

## Core concept

#### VIG-DOM-001 — What an item represents
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §1, v0.1 §1

A Vigila item MUST represent something that remains mentally or operationally
open: something to do, something being waited for, something requiring
follow-up, something to bring back later, something worth remembering until a
condition or date occurs, or a lightweight action that does not justify becoming
a ROS work item.

#### VIG-DOM-002 — Unique persistent identifier
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §1, v0.1 §1

Every item MUST have a unique persistent identifier. Identifier rules are
[VIG-PER-040](PERSISTENCE.md#vig-per-040) onward.

#### VIG-DOM-003 — Durability across sessions and devices
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §1

An item MUST survive application restarts and browser/device changes when
connected to its repository.

> v0.1 §1 stated this unconditionally; v0.2 qualifies it with "when connected to
> its repository", which is the governing form.

#### VIG-DOM-004 — Title is sufficient to create
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §1, §4

The minimum information required to create an item MUST be a title. Only the
minimum fields required to create an item may be mandatory. This matters for
conversational capture.

## Item kinds

#### VIG-DOM-005 — Supported kinds
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §85, v0.2 §2

Vigila MUST support these item kinds:

| Kind | Meaning | Example |
|---|---|---|
| `Task` | Something the user needs to do. | Send accountant the cancelled generator check. |
| `FollowUp` | Something the user needs to revisit. | Check with GC about the generator statement. |
| `Waiting` | Something dependent on another person, organisation, process, or event. | Waiting for CPA to respond. |

> **Override.** v0.1 §2 and v0.2 §2.4 also required a `Reminder` kind. v0.3 §85
> withdraws it: a reminder is behaviour attached to an item, not a kind, and is
> represented with follow-up/snooze timing instead. v0.2 §54 still lists
> Reminder in v1 scope, which is an unresolved conflict — see
> [OQ-01](OPEN-QUESTIONS.md#oq-01).

#### VIG-DOM-006 — Reminder behaviour without a Reminder kind
**Level:** SHOULD · **Release:** v1 · **Source:** v0.3 §85

Reminder behaviour SHOULD be represented using follow-up date, snooze, or
notification timing attached to a `Task` or `FollowUp`. A separate `Reminder`
kind SHOULD be reintroduced only if real workflows demonstrate a distinct
semantic need.

#### VIG-DOM-007 — Extensible kinds
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §2, v0.1 §2

The architecture SHOULD allow additional kinds later without requiring a
redesign of the persistence model.

## Item state

#### VIG-DOM-008 — Required states
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §3, v0.1 §3

Vigila MUST support exactly these initial workflow states: `Open`, `Waiting`,
`Deferred`, `Completed`, `Cancelled`.

#### VIG-DOM-009 — Legal transitions
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §3, v0.1 §3

The domain MUST permit these transitions and no others:

| From | To |
|---|---|
| `Open` | `Waiting`, `Deferred`, `Completed`, `Cancelled` |
| `Waiting` | `Open`, `Deferred`, `Completed`, `Cancelled` |
| `Deferred` | `Open`, `Waiting`, `Completed`, `Cancelled` |
| `Completed` | `Open` |
| `Cancelled` | `Open` |

#### VIG-DOM-010 — Transitions are explicit operations
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §3, v0.1 §3

State changes MUST occur through explicit domain transitions rather than
arbitrary property mutation.

#### VIG-DOM-011 — Transitions are auditable
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §3

Every state transition MUST be auditable.

> v0.1 §3 said "should be auditable"; v0.2 raises it to "must".

#### VIG-DOM-012 — Illegal transitions are prevented
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §3

Illegal transitions MUST be prevented by the domain model, not merely by the UI.
The corresponding error code is `InvalidTransition`
([VIG-AGT-050](AGENT.md#vig-agt-050)).

## Core fields

#### VIG-DOM-013 — Supported fields
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §4, v0.4 §102, §104, §105, v0.3 §73, §74

An item MUST support the following fields. Mandatory fields are marked; all
others are optional.

| Field | Mandatory | Defined by |
|---|---|---|
| ID | yes | [VIG-DOM-002](#vig-dom-002) |
| Title | yes | [VIG-DOM-014](#vig-dom-014) |
| Description / details | no | [VIG-DOM-016](#vig-dom-016) |
| Kind | yes | [VIG-DOM-005](#vig-dom-005) |
| Status | yes | [VIG-DOM-008](#vig-dom-008) |
| Created at | yes | [VIG-DOM-013](#vig-dom-013) |
| Created by (Actor) | yes | [VIG-DOM-035](#vig-dom-035) |
| CreatedVia | yes | [VIG-DOM-037](#vig-dom-037) |
| Updated at | yes | — |
| Completed at | no | — |
| Cancelled at | no | — |
| Due date/time | no | [VIG-TIME-001](TIME.md#vig-time-001) |
| Follow-up date/time | no | [VIG-TIME-004](TIME.md#vig-time-004) |
| Snooze until | no | [VIG-TIME-010](TIME.md#vig-time-010) |
| Next action | no | [VIG-DOM-021](#vig-dom-021) |
| Waiting on | no | [VIG-DOM-023](#vig-dom-023) |
| WaitingSince | no | [VIG-DOM-025](#vig-dom-025) |
| Tags | no | [VIG-DOM-026](#vig-dom-026) |
| Notes | no | [VIG-DOM-017](#vig-dom-017) |
| Source / context references | no | [VIG-DOM-030](#vig-dom-030) |
| History | yes | [VIG-DOM-032](#vig-dom-032) |
| LastActivityAt | yes | [VIG-DOM-036](#vig-dom-036) |
| Version / concurrency token | yes | [VIG-AGT-030](AGENT.md#vig-agt-030) |
| Important | no | [VIG-DOM-038](#vig-dom-038) |
| NeedsReview | no | [VIG-AGT-023](AGENT.md#vig-agt-023) |
| Resolution note | no | [VIG-DOM-039](#vig-dom-039) |
| Resolution classification | no | [VIG-DOM-040](#vig-dom-040) |

> v0.2 §4 lists the base set and adds "version/concurrency value where needed".
> `CreatedVia`, `LastActivityAt`, `WaitingSince`, `Important`, `NeedsReview`,
> and the resolution fields come from v0.3 and v0.4 and are elevated into v1 by
> §97 and §141.

## Title

#### VIG-DOM-014 — Every item has a title
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §5, v0.1 §5

Every item MUST have a concise human-readable title.

#### VIG-DOM-015 — Titles are editable and identity-preserving
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §5, v0.1 §5

Titles MUST be editable, and changing a title MUST NOT change item identity.

## Description

#### VIG-DOM-016 — Optional stable context
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §6, v0.1 §6

An item MAY contain a longer description. Descriptions MUST be optional and
editable. A description SHOULD represent relatively stable context; ongoing
developments SHOULD be captured as notes rather than by repeatedly rewriting the
description.

## Notes

#### VIG-DOM-017 — Notes are first-class
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §7, v0.1 §7

Notes MUST be a first-class Vigila concept. An item MAY contain zero or more
notes.

#### VIG-DOM-018 — Note fields
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §7, v0.1 §7

Each note MUST contain: Note ID, Item ID, Text, Created at, Created by.

#### VIG-DOM-019 — Notes are append-only
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §7, v0.1 §7

Notes SHOULD be append-only. Existing notes MUST NOT change silently. If note
editing is added later, edits MUST be auditable.

#### VIG-DOM-020 — Notes display chronologically and accept agent writes
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §7, v0.1 §7

The UI MUST display notes chronologically, and agents MUST be able to add notes.

> Worked example from v0.2 §7: "Add a note to the accountant follow-up that he
> asked for the cancelled check." The agent locates the item
> ([VIG-AGT-010](AGENT.md#vig-agt-010)) and appends the note.

## Next action

#### VIG-DOM-021 — Next action
**Level:** MAY · **Release:** v1 · **Source:** v0.2 §8, v0.1 §8

Every active item MAY contain a `NextAction` representing the immediate thing
required to move the item forward. The division of responsibility is: the title
describes the subject, the notes describe the history, the next action describes
what happens next.

#### VIG-DOM-022 — Next action visible without opening the item
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §8, v0.1 §8

The system SHOULD make the next action visible without requiring the user to
open the entire item. See [VIG-UI-007](UI.md#vig-ui-007).

## Waiting on

#### VIG-DOM-023 — Waiting-on field
**Level:** MAY · **Release:** v1 · **Source:** v0.2 §11, v0.1 §11

A waiting item MAY identify what or whom it is waiting on — for example a
person, a CPA, an event, an insurance company, a vendor, or a GitHub Action.

#### VIG-DOM-024 — Waiting-on stays lightweight
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §106, v0.2 §11

`WaitingOn` MUST remain a lightweight free-text field in v1 and MUST NOT require
a separate contacts system. The persistence and integration model MUST NOT make
assumptions that permanently prevent a future list of structured waiting
entities (person, organisation, system, process, event). Structured contacts are
explicitly not required in v1.

#### VIG-DOM-025 — WaitingSince
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §102

A `Waiting` item MUST expose `WaitingSince`:

- It MUST be set when an item enters `Waiting`.
- Leaving `Waiting` MUST preserve historical transition information.
- Re-entering `Waiting` MUST establish a new current `WaitingSince` while
  retaining prior history.
- It MUST support deterministic queries such as "items waiting more than N days".

## Tags

#### VIG-DOM-026 — User-defined tags
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §12, v0.1 §12

Tags MUST be supported and MUST be user-defined. An item MAY have zero or more
tags. Users MUST be able to add, remove, and rename tags.

#### VIG-DOM-027 — Tags are flat, not folders
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §12, v0.1 §12

Tags MUST NOT initially have hierarchy and MUST NOT behave like folders.

#### VIG-DOM-028 — Tag filtering and intersection
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §12, v0.1 §12

The system MUST support filtering by one or multiple tags, including
intersection queries such as "open items tagged culinary and sales". Agents MUST
be able to assign tags while creating or updating an item.

#### VIG-DOM-029 — Tag normalisation and identity
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §89, v0.4 §109, §110

Tags MUST be normalised consistently, and duplicate identical tags on one item
MUST NOT be stored. Tag identity SHOULD be conceptually separable from display
text; the implementation MAY initially store simple strings if migration to
stable `TagId`s remains straightforward. A future rename MUST be an explicit
operation that preserves semantic identity, not incidental string replacement,
and existing item history MUST remain understandable after a rename. Tag
metadata MUST NOT introduce a hierarchy by default.

> Richer tag metadata (stable `TagId`, description, archived state, aliases) is
> deferred by §142.

## Source and context references

#### VIG-DOM-030 — Source references
**Level:** MAY · **Release:** v1 · **Source:** v0.2 §13, v0.1 §13, v0.4 §107

An item MAY contain one or more source references — chat conversation, email,
GitHub issue or repository, website, document, ROS work item, Chrona entry,
external URL, or free-form reference. A reference MUST contain at least: Type,
Display name, External identifier or URI.

#### VIG-DOM-031 — References, not copies
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §107, v0.2 §13

Vigila MUST NOT require source content to be copied into the item, and MUST NOT
become a document-storage system. Items reference external files and documents;
Vigila stores metadata and reference information, never file contents. A
reference MUST allow the user or agent to return to the original context, and
SHOULD include stable identity when available rather than only a display URL.

## History

#### VIG-DOM-032 — Application-level audit history
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §14, v0.1 §14

Vigila MUST maintain an audit history for meaningful item changes, covering at
least: Created, Title changed, Status changed, Follow-up date changed, Due date
changed, Waiting-on changed, Next action changed, Tag added, Tag removed, Note
added, Completed, Reopened, Cancelled.

#### VIG-DOM-033 — History record contents
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §14, v0.1 §14

Each history record MUST contain: Timestamp, Actor, Operation, and relevant
old/new values where appropriate.

#### VIG-DOM-034 — Git history is not the semantic history
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §14

Git history MUST NOT be the application's only semantic history mechanism. Git
history MAY supplement application-level history.

> v0.1 §14 said "should not"; v0.2 raises it to "must not".

#### VIG-DOM-034a — History is not a diagnostic log
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §131

Item history MUST contain meaningful domain and user events only. Low-level
GitHub retries, HTTP status codes, transport failures, and stack traces MUST NOT
pollute user history; diagnostic logs MUST remain separate and MUST never expose
the GitHub token. User history MUST remain understandable to humans.

## Actor and origin

#### VIG-DOM-035 — Actor
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §15, v0.1 §15

Changes SHOULD identify their origin. An actor SHOULD have both an actor type
(Human, ChatGPT, Claude, automated process, integration) and an actor
identifier/name.

#### VIG-DOM-036 — LastActivityAt
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §73, §97

Each item MUST expose a deterministic `LastActivityAt` reflecting meaningful
activity: note added, state transition, next action changed, due or follow-up
date changed, waiting-on changed, or other substantive update. Read-only access
MUST NOT change it. Stale-item detection MUST rely on this value rather than
inferring activity from Git commit timestamps alone.

#### VIG-DOM-037 — CreatedVia
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §74, §97

`CreatedVia` MUST be recorded at creation with one of: `UI`, `Agent`,
`Integration`, `Import`. `Actor` identifies who or what initiated an operation;
`CreatedVia` identifies the interaction channel. Neither may expose secrets.

## Presentation metadata

#### VIG-DOM-038 — Important marker
**Level:** MAY · **Release:** v1 · **Source:** v0.4 §100

An item MAY be marked `Important`. It MUST be boolean in the initial model
rather than a multi-level priority scale, MUST NOT change workflow state, and
MUST affect presentation and filtering only. Important items MUST be filterable
and MAY be surfaced more prominently. Multi-level priority scoring is deferred
by §142.

`Important` is also the field that marks an item "for current attention", so it
brings an open item into the `Now` view ([VIG-UI-002](UI.md#vig-ui-002)) and
breaks ties within the last tier of that view's sort
([VIG-QRY-011](QUERY.md#vig-qry-011)).

> Both are presentation effects, not state changes: `Now` is a derived view and
> [VIG-TIME-024](TIME.md#vig-time-024) already treats view membership as
> calculated rather than stored. See [OQ-05](OPEN-QUESTIONS.md#oq-05).

## Closure metadata

#### VIG-DOM-039 — Resolution note
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §104, §141

Completing or cancelling an item MAY include an optional resolution note. Where
present it MUST be preserved permanently with the item, MUST be visible in
completed-item detail, MUST be searchable, and MUST be included in
human-readable export ([VIG-OPS-030](OPERATIONS.md#vig-ops-030)).

#### VIG-DOM-040 — Resolution classification
**Level:** MAY · **Release:** v1 · **Source:** v0.4 §105, OQ-09

Closure MAY carry an optional resolution classification from: `Superseded`,
`PromotedToRos`, `NoLongerRelevant`, `Other`.

It MUST remain optional metadata associated with closure, MUST NOT replace state
history, and MUST NOT complicate the core state model. Additional
classifications MAY be introduced later without changing item identity. Agents
MUST set a classification only when explicitly requested or deterministically
implied ([VIG-AGT-022](AGENT.md#vig-agt-022)).

> **Four classifications, not six.** §105 also lists `Cancelled` and
> `CompletedSuccessfully`, which duplicate the `Cancelled` and `Completed`
> workflow states exactly ([VIG-DOM-008](#vig-dom-008)). §105 itself says a
> classification must not replace state history, so where the state already
> carries the fact the classification adds only a second place to disagree —
> and leaves "deterministically implied" with no determinate answer.
>
> This is a recorded deviation from §105's literal list, taken in the
> reversible direction: restoring the two is additive, while removing them once
> items carry them is a migration. See
> [OQ-09](OPEN-QUESTIONS.md#oq-09).

## Completion, reopening, deletion

#### VIG-DOM-041 — Completion preserves everything
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §38, v0.1 §38

Completion MUST preserve notes, tags, history, sources, dates, and previous
status information. Completing an item MUST NOT delete it, and completed records
MUST remain searchable and referenceable.

#### VIG-DOM-042 — Reopening
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §39, v0.1 §39

Any completed or cancelled item MUST be capable of reopening unless doing so
violates a later domain rule. Reopening MUST append to history rather than erase
the prior completion.

#### VIG-DOM-043 — Deletion is not a normal workflow
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §40, v0.1 §40

Hard deletion MUST NOT be a normal workflow; cancellation or completion is used
instead. If permanent deletion exists it MUST require an explicit administrative
operation and MUST NOT occur accidentally through normal UI actions. Deletion
semantics MUST be documented honestly with respect to Git history
([VIG-PER-032](PERSISTENCE.md#vig-per-032)).

## Duplicates and relationships

#### VIG-DOM-044 — Duplicates are tolerated
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §25, v0.1 §25

Vigila MUST tolerate similar items, because legitimate duplicates exist, and
MUST NOT enforce unique titles. Agents MAY detect potential duplicates before
creating new items. A merge mechanism MAY be added later but is not required for
the initial release.

#### VIG-DOM-045 — Item relationships
**Level:** MAY · **Release:** deferred · **Source:** v0.3 §62, §98

Items MAY reference other Vigila items using relationship types such as
`RelatedTo`, `CreatedFrom`, `FollowUpTo`, `PromotedToRos`, `Replaces`.
Relationships SHOULD be directional where the semantics require it, MUST NOT
imply scheduling or blocking dependencies unless a later requirement defines
them, and MUST be auditable on creation and removal. Relationship support MUST
NOT evolve into a general dependency graph without demonstrated need.

#### VIG-DOM-046 — Context grouping
**Level:** MAY · **Release:** deferred · **Source:** v0.3 §63, §98

Vigila MAY eventually support an optional context/grouping concept distinct from
both tags and projects — tags classify an item; a context identifies the
specific subject, event, or body of work it belongs to. Context MUST NOT
introduce milestones, planning, dependencies, or resource allocation. The
initial version MAY rely on tags alone if that proves sufficient.

## Input validation

#### VIG-DOM-047 — Size limits and Unicode safety
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §89, §97

Titles, tags, notes, descriptions, and source references MUST have documented
practical size limits, generous enough for normal use while preventing
pathological payloads. Input MUST be Unicode-safe. Validation errors MUST be
explicit ([VIG-AGT-050](AGENT.md#vig-agt-050) `ValidationFailed`).

#### VIG-DOM-048 — Empty titles rejected
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §89

Empty or whitespace-only titles MUST be rejected.

#### VIG-DOM-049 — Invalid references do not corrupt items
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §89

Invalid URL or source formats MUST NOT corrupt item data.
