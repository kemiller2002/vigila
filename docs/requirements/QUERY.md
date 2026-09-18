---
id: REQ-QRY
title: Search, filtering, sorting, pagination, read models
status: draft
sources: v0.2 §20, §21, v0.3 §82, §83, v0.4 §108, §116–§120
---

# Query, search, and retrieval

## Search

#### VIG-QRY-001 — Search coverage
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §20, v0.1 §20

Search MUST cover at minimum: Title, Description, Notes, Tags, Waiting-on,
Source names. Resolution notes MUST also be searchable
([VIG-DOM-039](DOMAIN.md#vig-dom-039)).

#### VIG-QRY-002 — Search spans active and completed
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §20, v0.1 §20

Search MUST work across both active and completed items, and the user MUST be
able to limit search by status.

#### VIG-QRY-003 — Search ranking
**Level:** SHOULD · **Release:** v1 · **Source:** v0.4 §116

Search ranking SHOULD follow this order:

1. exact `ItemId` match
2. exact title match
3. strong title match
4. tag match
5. `WaitingOn` match
6. source/context match
7. note content
8. description content

Ranking behaviour MUST be documented. Results SHOULD indicate why an item
matched where practical. Search MUST avoid unpredictable ranking that changes
without data changes.

#### VIG-QRY-004 — Exact-ID lookup is separable
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §116

Agent callers MUST be able to request exact-ID lookup separately from fuzzy
search.

## Filtering

#### VIG-QRY-005 — Filter dimensions
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §21, v0.1 §21

Filters MUST support at least: Status, Kind, Tag, Due date, Follow-up date,
Waiting-on, Created date, Completed date. `Important`
([VIG-DOM-038](DOMAIN.md#vig-dom-038)) and `NeedsReview`
([VIG-AGT-023](AGENT.md#vig-agt-023)) MUST also be filterable.

#### VIG-QRY-006 — Do not expose filtering complexity prematurely
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §21, v0.1 §21

The UI SHOULD avoid exposing excessive filtering complexity until requested.

#### VIG-QRY-007 — Saved views
**Level:** MAY · **Release:** deferred · **Source:** v0.4 §108, §142, v0.2 §21

Common filters MAY eventually become saved views. A saved view MUST store a
query/filter definition rather than duplicating item data, MUST update
dynamically as item state changes, MUST NOT become a folder, and MUST be
optional and removable. Agent APIs SHOULD eventually be able to list or invoke
saved views.

## Sorting

#### VIG-QRY-010 — Deterministic sorting
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §82, §97

Primary views MUST have deterministic sort behaviour. Sorting MUST NOT depend
accidentally on repository file order, and sort rules MUST be testable.

#### VIG-QRY-011 — Now view sort policy
**Level:** SHOULD · **Release:** v1 · **Source:** v0.3 §82

The `Now` view SHOULD prioritise, in order:

1. overdue items
2. due today
3. follow-up date reached
4. pinned items
5. remaining currently actionable items

Within a category a secondary rule MAY use oldest-actionable-first, earliest
due/follow-up date, or another explicitly documented stable ordering.

> The policy references pinned items, which §72 classifies as a future
> enhancement. Until pinning exists, that tier is empty — see
> [OQ-04](OPEN-QUESTIONS.md#oq-04).

#### VIG-QRY-012 — UI sort options do not change domain semantics
**Level:** MAY · **Release:** future · **Source:** v0.3 §82

UI sorting options MAY be added later without changing underlying domain
semantics.

## Undated items

#### VIG-QRY-013 — Undated items stay discoverable
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §83, §97

Open items with no due date, follow-up date, or snooze date MUST remain
discoverable and MUST NOT disappear indefinitely. A review mechanism SHOULD
periodically surface undated items. Search and filter MUST be able to identify
undated active items, and agent summaries MUST be able to ask for open items
with no review date.

#### VIG-QRY-014 — Undated does not mean Now
**Level:** SHOULD · **Release:** v1 · **Source:** v0.3 §83

Undated items do not necessarily belong in the `Now` view every day.

## Pagination

#### VIG-QRY-015 — Pagination from the beginning
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §118, §141

List and search APIs MUST support pagination from the beginning. Agent APIs MUST
NOT assume the entire result set fits in one response. Page size limits MUST be
documented.

#### VIG-QRY-016 — Cursor-based preferred
**Level:** SHOULD · **Release:** v1 · **Source:** v0.4 §118

Pagination SHOULD use deterministic ordering, and cursor-based pagination SHOULD
be preferred where it improves stability.

#### VIG-QRY-017 — Pagination tokens carry no secrets
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §118

Pagination tokens and cursors MUST NOT expose secrets.

#### VIG-QRY-018 — Stable pagination order
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §119

Queries MUST have deterministic secondary sorting. Cursor behaviour MUST be
defined for records created or changed while paging, so agent callers can safely
continue traversal. A snapshot-like query model MAY be considered later if
concurrent change proves problematic.

## Read models

#### VIG-QRY-020 — Read models separate from the write model
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §120

Vigila MUST expose lightweight summary/read models for lists and search, with
richer data available from item detail. Full history and notes MUST NOT be
loaded unnecessarily for every list. Read models MUST be derived from canonical
domain data, and read optimisation MUST NOT become a second source of truth.

#### VIG-QRY-021 — Compact search result
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §117, §141

A compact result MUST include at least: `ItemId`, title, state, kind, tags, due
date, follow-up date, `WaitingOn`, `LastActivityAt`, version/concurrency token.

Compact results MUST NOT include full notes or history by default; full detail
MUST be retrievable separately. Compact results MUST be stable and
machine-readable.
