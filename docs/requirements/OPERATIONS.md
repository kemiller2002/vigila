---
id: REQ-OPS
title: Review, notifications, staleness, export, metrics, automation limits
status: draft
sources: v0.2 §34–§37, §41, §51, v0.3 §71, §72, §84, v0.4 §134–§137
---

# Operational behaviour

## Daily review and agent summaries

#### VIG-OPS-001 — Daily review
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §35, v0.1 §35

Vigila SHOULD support an easy daily-review experience. An agent MUST be able to
ask "give me everything I need to follow up on today" and get a deterministic
answer, rather than retrieving the entire database and inferring it.

#### VIG-OPS-002 — Agent summary queries
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §36, v0.1 §36

The API MUST support efficient retrieval for summaries including:

- What needs attention today?
- What am I waiting on?
- What is overdue?
- What changed recently?
- What open items relate to a given tag?
- What hasn't been touched in 30 days?

These MUST be possible without agents needing direct knowledge of storage
internals.

#### VIG-OPS-003 — Review mode
**Level:** SHOULD · **Release:** deferred · **Source:** v0.3 §84, §98

Vigila SHOULD eventually provide a structured review workflow covering overdue,
waiting-too-long, no-date, stale, and deferred items whose review date has
arrived — with actions for complete, add note, follow up, snooze, defer, cancel,
and promote to ROS.

Review MUST work from deterministic item state rather than agent inference, MUST
NOT modify an item merely by viewing it, and MUST NOT complicate the core item
model unnecessarily.

> §84 classifies this as a future enhancement with high value.

## Stale items

#### VIG-OPS-004 — Stale-item detection
**Level:** SHOULD · **Release:** future · **Source:** v0.2 §37, v0.1 §37

The system SHOULD eventually identify items that have remained open without
activity for a configurable period. Possible criteria: no notes, no state
changes, no due date, no follow-up date, no updates for 30 days.

#### VIG-OPS-005 — Surface, never auto-resolve
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §37, v0.1 §37

Stale items MUST be surfaced during review rather than automatically deleted or
completed.

#### VIG-OPS-006 — Deterministic staleness
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §73

Stale-item detection MUST rely on deterministic activity data
([VIG-DOM-036](DOMAIN.md#vig-dom-036)) rather than inference from Git commit
timestamps alone.

## No hidden automation

#### VIG-OPS-007 — Visibility over silent mutation
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §135

Vigila MAY surface recommendations and MAY identify stale, overdue, or
waiting-too-long items. It MUST NOT automatically complete, cancel, reclassify,
delete, or promote items without an explicit configured rule or authorized
operation. Default behaviour MUST favour visibility over silent mutation.

## Notifications

#### VIG-OPS-010 — No notification platform in v1
**Level:** MAY · **Release:** future · **Source:** v0.2 §34, v0.1 §34

The first version does not need a complete notification platform. The
architecture SHOULD permit future mechanisms: browser notification, email, agent
summary, daily digest, calendar integration.

#### VIG-OPS-011 — Storing a date is not delivering a notification
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §34, v0.1 §34

The distinction between storing a follow-up date and delivering an external
notification MUST remain explicit.

#### VIG-OPS-012 — Notification deduplication
**Level:** MUST · **Release:** deferred · **Source:** v0.4 §136, §142

A single due or follow-up event MUST NOT produce repeated notifications
unintentionally. Notification identity and delivery state MUST be separate from
item workflow state. Re-notification policies, if supported, MUST be explicit.
Notification delivery failure MUST NOT alter the underlying item.

#### VIG-OPS-013 — Notification acknowledgement is separate
**Level:** MUST · **Release:** deferred · **Source:** v0.4 §137, §142

Notification delivered/seen/acknowledged state MUST remain separate from
completion, follow-up state, and waiting state. Opening a notification MUST NOT
automatically complete or modify an item. Acknowledgement MUST be optional
presentation/integration metadata.

## Archive and pinning

#### VIG-OPS-020 — Soft archive
**Level:** MAY · **Release:** deferred · **Source:** v0.3 §71, §98

Completed items MAY eventually be archived so older completed items do not
dominate normal browsing. Archive SHOULD be a presentation/retention flag rather
than a new core workflow state. Archived items MUST remain searchable, archiving
MUST NOT remove history, and reopening an archived completed item MUST restore
it to an active, visible state appropriately.

#### VIG-OPS-021 — Pinned items
**Level:** MAY · **Release:** deferred · **Source:** v0.3 §72, §98

An item MAY optionally be pinned. Pinning MUST NOT change the item's workflow
state. Pinned items SHOULD be surfaced prominently in appropriate views, and
pinning/unpinning SHOULD be auditable if history granularity warrants it.

> §72 classifies this as a low-cost future enhancement. The `Now` sort policy
> ([VIG-QRY-011](QUERY.md#vig-qry-011)) already reserves a tier for pinned
> items — see [OQ-04](OPEN-QUESTIONS.md#oq-04).

## Export and import

#### VIG-OPS-030 — Machine-readable export
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §41, v0.1 §41, v0.3 §88

Vigila data MUST NOT be trapped inside the application. The system MUST support
export into a documented machine-readable format; the canonical record format
MAY satisfy much of this requirement. Export MUST NOT include the GitHub token.
Repository history is not a substitute for a documented data export contract.

#### VIG-OPS-031 — Human-readable export
**Level:** SHOULD · **Release:** future · **Source:** v0.4 §134, v0.2 §41

A human-readable export (plain text or Markdown) SHOULD be possible, able to
include title, state, dates, tags, `WaitingOn`, next action, description, notes,
source references, resolution, and history where requested.

It MUST never include the GitHub token, and SHOULD make it easy to hand context
to another person or agent.

#### VIG-OPS-032 — Import safety
**Level:** MUST · **Release:** future · **Source:** v0.3 §88

Import MUST validate schema and item identities before mutation and MUST NOT
silently overwrite newer items. Import SHOULD support dry-run/validation before
commit if bulk import is added.

#### VIG-OPS-033 — Backup preserves identity
**Level:** SHOULD · **Release:** future · **Source:** v0.3 §88

A future backup/restore operation SHOULD preserve item IDs and history semantics
where possible.

## Metrics

#### VIG-OPS-040 — Vigila is not an analytics product
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §51, v0.1 §51

Vigila MUST NOT be turned into an analytics product.

#### VIG-OPS-041 — Operational metrics
**Level:** MAY · **Release:** future · **Source:** v0.2 §51, v0.1 §51

Basic operational metrics MAY eventually include open items, overdue items,
waiting items, completed items, average age, items with no follow-up date, and
stale items.

#### VIG-OPS-042 — Review and health, not surveillance
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §51, v0.1 §51

Metrics MUST support review and system health rather than productivity
surveillance.
