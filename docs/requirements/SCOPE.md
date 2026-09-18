---
id: REQ-SCOPE
title: v1 scope, non-goals, and deferred backlog
status: draft
sources: v0.2 §52, §53, §54, v0.3 §97, §98, v0.4 §141, §142
---

# Scope

## v1 scope

#### VIG-SCOPE-001 — Baseline v1 scope
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §54

The initial Vigila application MUST include:

**Core item operations** — create, edit, complete, reopen, cancel, defer.

**Kinds** — Task, FollowUp, Waiting.
*(v0.2 §54 also lists Reminder; withdrawn by v0.3 §85 — see
[OQ-01](OPEN-QUESTIONS.md#oq-01).)*

**Core data** — title, description, next action, due date, follow-up date,
waiting on, tags, append-only notes, source links, history.

**Views** — Now, Waiting, Upcoming, Deferred, Completed.

**Interaction** — search, filter, fast capture, quick completion,
mobile-friendly use.

**Agent support** — agent integration API, natural-language-to-explicit-command
workflow.

**Persistence and architecture** — GitHub repository persistence, user-supplied
GitHub token, user-supplied `owner/repository`, optional branch defaulting to
`main`, token/repository/branch in browser `localStorage`, connection validation
before loading, integration assembly, F# domain, Limen UI, ROS-governed
development, SDE-governed design.

#### VIG-SCOPE-002 — Elevated into v1 by §97
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §97

These MUST be part of the first implementation, because deferring them would
create architectural debt:

| Elevated requirement | Where specified |
|---|---|
| snooze semantics | [VIG-TIME-010](TIME.md#vig-time-010) |
| explicit date/time/date-only semantics | [VIG-TIME-017](TIME.md#vig-time-017) |
| time-zone-safe presentation | [VIG-TIME-014](TIME.md#vig-time-014) |
| repository initialisation | [VIG-PER-010](PERSISTENCE.md#vig-per-010) |
| application manifest | [VIG-PER-013](PERSISTENCE.md#vig-per-013) |
| configurable storage path | [VIG-PER-004](PERSISTENCE.md#vig-per-004) |
| agent `OperationId` / idempotency | [VIG-AGT-012](AGENT.md#vig-agt-012) |
| deterministic mutation receipts | [VIG-AGT-014](AGENT.md#vig-agt-014) |
| narrowly scoped agent operations | [VIG-AGT-020](AGENT.md#vig-agt-020) |
| optimistic concurrency | [VIG-AGT-030](AGENT.md#vig-agt-030) |
| Git commit conventions | [VIG-PER-033](PERSISTENCE.md#vig-per-033) |
| Git history / privacy awareness | [VIG-PER-032](PERSISTENCE.md#vig-per-032) |
| `LastActivityAt` | [VIG-DOM-036](DOMAIN.md#vig-dom-036) |
| `CreatedVia` | [VIG-DOM-037](DOMAIN.md#vig-dom-037) |
| capability discovery | [VIG-AGT-040](AGENT.md#vig-agt-040) |
| health/status operation | [VIG-AGT-041](AGENT.md#vig-agt-041) |
| read/write capability validation | [VIG-SEC-009](SECURITY.md#vig-sec-009) |
| branch / write-rule handling | [VIG-SEC-014](SECURITY.md#vig-sec-014) |
| malformed-record isolation | [VIG-PER-030](PERSISTENCE.md#vig-per-030) |
| unknown-field tolerance | [VIG-PER-022](PERSISTENCE.md#vig-per-022) |
| schema migration rules | [VIG-PER-023](PERSISTENCE.md#vig-per-023) |
| deterministic sorting | [VIG-QRY-010](QUERY.md#vig-qry-010) |
| undated-item discoverability | [VIG-QRY-013](QUERY.md#vig-qry-013) |
| browser security for `localStorage` token | [VIG-SEC-013](SECURITY.md#vig-sec-013) |
| token-storage UX | [VIG-SEC-006](SECURITY.md#vig-sec-006) |
| identifier rules | [VIG-PER-040](PERSISTENCE.md#vig-per-040) |
| GitHub rate-limit handling | [VIG-PER-046](PERSISTENCE.md#vig-per-046) |
| unsaved-edit protection | [VIG-UI-013](UI.md#vig-ui-013) |
| extended testing requirements | [VIG-TST-010](QUALITY.md#vig-tst-010) |

#### VIG-SCOPE-003 — Elevated into v1 by §141
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §141

| Elevated requirement | Where specified |
|---|---|
| `WaitingSince` | [VIG-DOM-025](DOMAIN.md#vig-dom-025) |
| optional resolution/outcome note | [VIG-DOM-039](DOMAIN.md#vig-dom-039) |
| common command envelope | [VIG-AGT-035](AGENT.md#vig-agt-035) |
| machine-readable error taxonomy | [VIG-AGT-050](AGENT.md#vig-agt-050) |
| retryability indicator | [VIG-AGT-051](AGENT.md#vig-agt-051) |
| read-before-write concurrency | [VIG-AGT-033](AGENT.md#vig-agt-033) |
| compact search result DTO | [VIG-QRY-021](QUERY.md#vig-qry-021) |
| pagination | [VIG-QRY-015](QUERY.md#vig-qry-015) |
| explicit clock abstraction | [VIG-TIME-023](TIME.md#vig-time-023) |
| derived-state calculation rules | [VIG-TIME-024](TIME.md#vig-time-024) |
| capture-and-continue | [VIG-UI-012](UI.md#vig-ui-012) |
| source references by identity, not file storage | [VIG-DOM-031](DOMAIN.md#vig-dom-031) |
| unsaved text preservation | [VIG-UI-013](UI.md#vig-ui-013) |

## Non-goals

#### VIG-SCOPE-010 — v1 non-goals
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §52, v0.1 §52

Vigila v1 MUST NOT attempt to provide:

| | | |
|---|---|---|
| Project planning | Kanban boards | Gantt charts |
| Sprint planning | Story points | Work estimation |
| Resource allocation | Complex dependencies | Team performance measurement |
| CRM functionality | Contact management | Document storage |
| Email client functionality | Calendar replacement | ROS replacement |
| Automated AI decision-making | Shared credential management across Echelon apps | Multi-token credential bundles |
| Centralized cross-application connection profiles | | |

Integrations MAY reference or invoke those systems without reproducing them.

> The last three entries are added by v0.2 §52 and are not in v0.1 §52.

## Deferred backlog

#### VIG-SCOPE-020 — Deferred by §98
**Level:** MAY · **Release:** deferred · **Source:** v0.3 §98

Intentionally deferred unless an early workflow proves them necessary:

| Deferred | Where specified |
|---|---|
| recurring items | [VIG-TIME-025](TIME.md#vig-time-025) |
| item relationships | [VIG-DOM-045](DOMAIN.md#vig-dom-045) |
| context grouping beyond tags | [VIG-DOM-046](DOMAIN.md#vig-dom-046) |
| bulk operations | [VIG-AGT-070](AGENT.md#vig-agt-070) |
| soft archive | [VIG-OPS-020](OPERATIONS.md#vig-ops-020) |
| pinned items | [VIG-OPS-021](OPERATIONS.md#vig-ops-021) |
| full Review mode | [VIG-OPS-003](OPERATIONS.md#vig-ops-003) |
| pull-request-based persistence | [VIG-SEC-016](SECURITY.md#vig-sec-016) |
| richer notification delivery | [VIG-OPS-010](OPERATIONS.md#vig-ops-010) |
| shared Echelon credential/repository configuration | [VIG-SEC-030](SECURITY.md#vig-sec-030) |
| multiple repository/token profiles | [VIG-SEC-030](SECURITY.md#vig-sec-030) |
| JSON credential bundles | [VIG-SEC-030](SECURITY.md#vig-sec-030) |
| full offline synchronisation | [VIG-PER-060](PERSISTENCE.md#vig-per-060) |

#### VIG-SCOPE-021 — Deferred by §142
**Level:** MAY · **Release:** deferred · **Source:** v0.4 §142

| Deferred | Where specified |
|---|---|
| multi-level priority | [VIG-DOM-038](DOMAIN.md#vig-dom-038) |
| manual list ordering | [VIG-UI-031](UI.md#vig-ui-031) |
| escalation date | [VIG-TIME-026](TIME.md#vig-time-026) |
| structured multiple `WaitingOn` entities | [VIG-DOM-024](DOMAIN.md#vig-dom-024) |
| saved views | [VIG-QRY-007](QUERY.md#vig-qry-007) |
| rich tag metadata / aliasing | [VIG-DOM-029](DOMAIN.md#vig-dom-029) |
| global command palette | [VIG-UI-030](UI.md#vig-ui-030) |
| advanced Inbox customisation | [VIG-UI-006a](UI.md#vig-ui-006a) |
| repository migration UI | [VIG-PER-008](PERSISTENCE.md#vig-per-008) |
| atomic multi-system transactions | — |
| notification delivery/acknowledgement | [VIG-OPS-012](OPERATIONS.md#vig-ops-012) |
| extensive cross-application reference resolution | [VIG-AGT-045](AGENT.md#vig-agt-045) |

> "Atomic multi-system transactions" has no corresponding requirement because no
> source section specifies one; it appears only in the §142 deferral list.
> [VIG-AGT-024](AGENT.md#vig-agt-024) covers atomic composite mutations *within*
> Vigila, which is a different thing and is in v1.
