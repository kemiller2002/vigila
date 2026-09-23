---
id: REQ-INDEX
title: Vigila requirements — index and conventions
status: draft
version: 0.1.0
created: 2026-09-17
updated: 2026-09-23
sources:
  - input-documents/vigila_requirements_v0.1.txt
  - input-documents/Vigila_Requirements_v0.2.txt
  - input-documents/Vigila_Additional_Requirements_v0.3.txt
  - input-documents/Vigila_Additional_Requirements_v0.4.txt
  - input-documents/Vigila_Additional_Requirements_v0.5.txt
---

# Vigila requirements

This directory holds the consolidated, traceable requirements derived from the
five source documents in [`input-documents/`](../../input-documents/). It is a
*derived* artifact: the source documents remain the record of what was written,
and this set is the record of what is normative.

Nothing here adds scope. Every requirement traces to at least one source
section. Where the derivation required a judgement call — because two documents
disagree, or because a statement was ambiguous — that call is recorded in
[`OPEN-QUESTIONS.md`](OPEN-QUESTIONS.md) rather than being silently resolved.

## Documents

| Document | Covers | Source sections |
|---|---|---|
| [`GOVERNANCE.md`](GOVERNANCE.md) | Purpose, engineering standards, simplicity, feature admission | §0, §99, §143 |
| [`DOMAIN.md`](DOMAIN.md) | Items, kinds, states, fields, notes, tags, sources, history | §1–§15, §85, §100, §102, §104–§107, §109, §110 |
| [`TIME.md`](TIME.md) | Dates, time zones, snooze, clock, derived state | §9, §10, §56–§58, §121–§123 |
| [`UI.md`](UI.md) | Views, presentation, capture, completion, accessibility, mobile | §16–§19, §42, §43, §93, §94, §111–§113 |
| [`QUERY.md`](QUERY.md) | Search, filtering, sorting, pagination, read models | §20, §21, §82, §83, §108, §116–§120 |
| [`AGENT.md`](AGENT.md) | Agent API, idempotency, receipts, concurrency, errors, ROS promotion | §22–§25, §65–§68, §74, §75, §114, §115, §126–§130, §138–§140 |
| [`PERSISTENCE.md`](PERSISTENCE.md) | Storage, repository layout, schema, migration, Git semantics | §26–§29, §59–§61, §69, §70, §79–§81, §88–§92, §124, §125, §132, §133 |
| [`SECURITY.md`](SECURITY.md) | Authentication, token handling, browser security, authorization | §47, §48, §70, §86, §87 |
| [`OPERATIONS.md`](OPERATIONS.md) | Notifications, review, stale items, metrics, export, automation limits | §34–§37, §41, §51, §84, §134–§137 |
| [`QUALITY.md`](QUALITY.md) | Testing and documentation obligations | §49, §50, §95, §96 |
| [`PLATFORM.md`](PLATFORM.md) | Required shared platform dependencies: Aegis, Forma, Folio, and their ownership boundaries | §144–§151 |
| [`SCOPE.md`](SCOPE.md) | v1 scope, non-goals, deferred backlog | §52, §53, §54, §97, §98, §141, §142 |
| [`TRACEABILITY.md`](TRACEABILITY.md) | Source section → requirement ID map, both directions | all |
| [`OPEN-QUESTIONS.md`](OPEN-QUESTIONS.md) | Decisions taken where the sources conflicted or were silent (all resolved) | — |

## Identifier scheme

```
VIG-<AREA>-<NNN>
```

`AREA` is one of `GOV`, `DOM`, `TIME`, `UI`, `QRY`, `AGT`, `PER`, `SEC`, `OPS`,
`TST`, `SCOPE`, `PLAT`. Numbers are assigned in document order and are **stable**: a
requirement that is withdrawn keeps its number and is marked `Withdrawn` rather
than being reused or renumbered.

## Requirement format

Each requirement is a block:

```
#### VIG-DOM-014 — Title changes preserve identity
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §5, v0.1 §5

Changing an item's title MUST NOT change its ItemId.
```

### Level

Normative strength, mapped from the source documents' own wording:

| Level | Meaning | Source wording |
|---|---|---|
| **MUST** | Required. Absence is a defect. | "must", "required", "never" |
| **SHOULD** | Expected unless there is a recorded reason not to. | "should", "preferably" |
| **MAY** | Permitted. Carries no obligation to build. | "may", "can", "optionally" |

Where a source says "should" about something structural that cannot be retrofitted
(for example, a schema-version field), the level is recorded as stated and the
consequence is noted in the requirement text. The level was not silently
strengthened.

### Release

| Value | Meaning |
|---|---|
| `v1` | In the first implementation. |
| `deferred` | Explicitly deferred by §98 or §142, or classified as a future upgrade at its point of definition. |
| `future` | Described as eventual with no release assigned. |

Requirements elevated into v1 by §97 or §141 carry `v1` and cite the elevating
section alongside their defining section.

### Source

The document and section the requirement derives from. `v0.1` … `v0.4` are the
four input documents in version order. Multiple citations are listed
newest-first, so the governing statement is the one named first.

## Precedence

Both v0.3 and v0.4 state that where they change an earlier requirement, the
newer text governs. Applied in order:

```
v0.5  >  v0.4  >  v0.3  >  v0.2  >  v0.1
```

v0.5 adds mandatory shared-platform implementation requirements for Aegis, Forma, and Folio. It does not replace the earlier functional requirements; it constrains how those requirements are implemented.

v0.2 is a near-complete restatement of v0.1 with §47 (authentication) added and
several sections tightened; v0.1 is cited only where it says something v0.2
dropped. v0.3 and v0.4 are additive extension documents that also override
specific earlier sections.

Three overrides materially change earlier requirements:

1. **§85 removes the `Reminder` item kind** that v0.1 §2 and v0.2 §2.4 required.
   Reminder behaviour becomes follow-up/snooze timing on a Task or FollowUp.
   This conflicts with v0.2 §54, which still lists Reminder in v1 scope — see
   [OQ-01](OPEN-QUESTIONS.md#oq-01).
2. **§56 separates snooze from defer.** Deferred remains a workflow state;
   snooze becomes presentation timing and is explicitly *not* a state.
3. **§97 and §141 promote a large set of previously "future" requirements into
   v1** on the grounds that deferring them would create architectural debt.

## Deriving further work

These documents state *what* Vigila must do. They do not schedule the work, size
it, or assign it. Turning a requirement into engineering work goes through the
ROS work protocol (`docs/work-protocol.md`) and, for structural decisions, the
SDE method under `.sde/`.
