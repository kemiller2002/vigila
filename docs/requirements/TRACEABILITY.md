---
id: REQ-TRACE
title: Source section to requirement traceability
status: draft
created: 2026-09-18
updated: 2026-09-26
provenance:
  contributions:
    EXE-20260926T081409758Z-615c839a:
      operations: [modified]
      at: 2026-09-26T08:17:42.022Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Index and trace the VIG-PROV area (FEAT-ECHELON-PROVENANCE)"
---

# Traceability

Every numbered section of the four source documents maps to at least one
requirement. This table is the coverage proof: a section with no requirement
would be a dropped requirement.

Sections §0–§54 exist in both v0.1 and v0.2. v0.2 governs; v0.1 is cited in a
requirement only where it says something v0.2 dropped or stated differently.
Sections §55–§99 are v0.3. Sections §100–§143 are v0.4. Sections §144–§151 are v0.5.

## v0.2 (with v0.1) — §0–§54

| § | Topic | Requirements |
|---|---|---|
| §0.1 | Purpose | `GOV-001`–`GOV-005` |
| §0.2 | Engineering standards | `GOV-006`–`GOV-015` |
| §0.3 | Simplicity | `GOV-016`–`GOV-019` |
| §1 | Core concept | `DOM-001`–`DOM-004` |
| §2 | Item types | `DOM-005`–`DOM-007` |
| §3 | Item state | `DOM-008`–`DOM-012` |
| §4 | Identity and core fields | `DOM-013` |
| §5 | Title | `DOM-014`, `DOM-015` |
| §6 | Description | `DOM-016` |
| §7 | Notes | `DOM-017`–`DOM-020` |
| §8 | Next action | `DOM-021`, `DOM-022` |
| §9 | Due dates | `TIME-001`–`TIME-003` |
| §10 | Follow-up dates | `TIME-004`, `TIME-005` |
| §11 | Waiting on | `DOM-023`, `DOM-024` |
| §12 | Tags | `DOM-026`–`DOM-029` |
| §13 | Source references | `DOM-030`, `DOM-031` |
| §14 | History | `DOM-032`–`DOM-034` |
| §15 | Actor | `DOM-035` |
| §16 | Primary views | `UI-001`–`UI-006` |
| §17 | Item presentation | `UI-007`, `UI-008` |
| §18 | Quick completion | `UI-009` |
| §19 | Fast capture | `UI-010`, `UI-011` |
| §20 | Search | `QRY-001`, `QRY-002` |
| §21 | Filtering | `QRY-005`–`QRY-007` |
| §22 | Agent interface | `AGT-001`–`AGT-003` |
| §23 | Natural-language capture | `AGT-004`, `AGT-005` |
| §24 | Ambiguity handling | `AGT-010`, `AGT-011` |
| §25 | Duplicate handling | `DOM-044` |
| §26 | Persistence | `PER-001`, `PER-002` |
| §27 | Repository organisation | `PER-003` |
| §28 | Record format | `PER-020`, `PER-021` |
| §29 | Concurrency | `AGT-030`, `AGT-034` |
| §30 | Integration assembly | `AGT-042`, `AGT-043` |
| §31 | Integration boundaries | `AGT-044` |
| §32 | ROS integration | `GOV-004`, `GOV-005`, `AGT-060`, `AGT-061` |
| §33 | Events | `AGT-046` |
| §34 | Notifications | `OPS-010`, `OPS-011` |
| §35 | Daily review | `OPS-001` |
| §36 | Agent summaries | `OPS-002` |
| §37 | Stale items | `OPS-004`, `OPS-005` |
| §38 | Completion | `DOM-041` |
| §39 | Reopening | `DOM-042` |
| §40 | Deletion | `DOM-043` |
| §41 | Data export | `OPS-030`, `OPS-031` |
| §42 | Accessibility | `UI-020` |
| §43 | Mobile usability | `UI-023` |
| §44 | Performance | `UI-024`–`UI-026`, `PER-045` |
| §45 | Offline behaviour | `PER-060`, `UI-014` |
| §46 | Failure handling | `UI-015` |
| §47.1–.2 | Required inputs | `SEC-001` |
| §47.3 | Local storage | `SEC-002` |
| §47.4 | Token handling | `SEC-005` |
| §47.5 | Startup behaviour | `SEC-007` |
| §47.6 | Setup UI | `SEC-003` |
| §47.7 | Connection validation | `SEC-008` |
| §47.8 | Reconfiguration | `SEC-004` |
| §47.9 | Architectural boundary | `SEC-020` |
| §47.10 | Connection failures | `SEC-012` |
| §47.11 | v1 auth decision | `SEC-021` |
| §48 | Authorization | `SEC-021`, `SEC-022` |
| §49 | Testing | `TST-001`–`TST-004` |
| §50 | Documentation | `TST-020`, `TST-022` |
| §51 | Metrics | `OPS-040`–`OPS-042` |
| §52 | Non-goals | `SCOPE-010` |
| §53 | Shared connection config | `SEC-030` |
| §54 | v1 scope | `SCOPE-001` |

## v0.3 — §55–§99

| § | Topic | Requirements |
|---|---|---|
| §55 | Recurring follow-ups | `TIME-025` |
| §56 | Snooze versus defer | `TIME-010`–`TIME-013` |
| §57 | Time and time-zone semantics | `TIME-014`–`TIME-019` |
| §58 | NL date interpretation | `TIME-020`–`TIME-022` |
| §59 | Repository initialization | `PER-010`–`PER-012` |
| §60 | Application manifest | `PER-013`–`PER-015` |
| §61 | Configurable storage path | `PER-004`–`PER-006` |
| §62 | Item relationships | `DOM-045` |
| §63 | Context grouping | `DOM-046` |
| §64 | Bulk operations | `AGT-070` |
| §65 | Agent idempotency | `AGT-012`, `AGT-013` |
| §66 | Operation receipts | `AGT-014`, `AGT-015` |
| §67 | Narrow mutation semantics | `AGT-020`, `AGT-021` |
| §68 | Optimistic concurrency | `AGT-030`–`AGT-032` |
| §69 | Git commit semantics | `PER-033`, `PER-034` |
| §70 | Git history and privacy | `PER-032`, `SEC-005` |
| §71 | Soft archive | `OPS-020` |
| §72 | Pinned items | `OPS-021` |
| §73 | Last activity | `DOM-036`, `OPS-006` |
| §74 | CreatedVia / origin | `DOM-037` |
| §75 | Capability discovery | `AGT-040` |
| §76 | Health / status | `AGT-041` |
| §77 | Repository capability validation | `SEC-009`–`SEC-011` |
| §78 | Branch rules | `SEC-014`–`SEC-016` |
| §79 | Malformed record handling | `PER-030`, `PER-031` |
| §80 | Unknown fields | `PER-022` |
| §81 | Schema migration rules | `PER-023` |
| §82 | Deterministic sorting | `QRY-010`–`QRY-012` |
| §83 | Undated items | `QRY-013`, `QRY-014` |
| §84 | Review mode | `OPS-003` |
| §85 | Item type refinement | `DOM-005`, `DOM-006` |
| §86 | Browser security | `SEC-005`, `SEC-013` |
| §87 | Token storage UX | `SEC-006` |
| §88 | Import / export / backup | `OPS-030`, `OPS-032`, `OPS-033` |
| §89 | Input size and validation | `DOM-029`, `DOM-047`–`DOM-049` |
| §90 | Identifier rules | `PER-040`–`PER-043` |
| §91 | Write batching / API efficiency | `PER-044`–`PER-046` |
| §92 | Rate limit / remote change | `PER-046`–`PER-048` |
| §93 | Unsaved edit protection | `UI-013` |
| §94 | Keyboard-first capture | `UI-021`, `UI-022` |
| §95 | Testing extensions | `TST-010`, `TST-014`–`TST-017` |
| §96 | Documentation extensions | `TST-021` |
| §97 | v1 elevations | `SCOPE-002` |
| §98 | Deferred backlog | `SCOPE-020` |
| §99 | Do not turn Vigila into ROS | `GOV-020` |

## v0.4 — §100–§143

| § | Topic | Requirements |
|---|---|---|
| §100 | Importance marker | `DOM-038` |
| §101 | Manual ordering | `UI-031` |
| §102 | WaitingSince | `DOM-025` |
| §103 | Escalation date | `TIME-026` |
| §104 | Resolution note | `DOM-039` |
| §105 | Resolution classification | `DOM-040`, `AGT-022` |
| §106 | Multiple waiting entities | `DOM-024` |
| §107 | Attachments by reference | `DOM-030`, `DOM-031` |
| §108 | Saved searches / views | `QRY-007` |
| §109 | Tag metadata | `DOM-029` |
| §110 | Tag rename and alias safety | `DOM-029` |
| §111 | Global command palette | `UI-030` |
| §112 | Capture and continue | `UI-012` |
| §113 | Inbox / unprocessed view | `UI-006a` |
| §114 | Agent-created item review | `AGT-023` |
| §115 | Agent uncertainty | `AGT-011a` |
| §116 | Search ranking | `QRY-003`, `QRY-004` |
| §117 | Compact search result | `QRY-021` |
| §118 | Pagination | `QRY-015`–`QRY-017` |
| §119 | Stable pagination order | `QRY-018` |
| §120 | Read models | `QRY-020` |
| §121 | Derived state | `TIME-024` |
| §122 | Explicit clock abstraction | `TIME-023` |
| §123 | Agent time-zone context | `TIME-019`, `TIME-021` |
| §124 | Workspace metadata | `PER-007` |
| §125 | Repository relocation | `PER-008` |
| §126 | Read-before-write | `AGT-033` |
| §127 | Atomic composite mutations | `AGT-024` |
| §128 | Common command envelope | `AGT-035` |
| §129 | Error taxonomy | `AGT-050` |
| §130 | Retryability marker | `AGT-051` |
| §131 | History versus diagnostic logs | `DOM-034a` |
| §132 | Workspace validation | `PER-050` |
| §133 | Repair must be explicit | `PER-031` |
| §134 | Human-readable export | `OPS-031` |
| §135 | No hidden automation | `OPS-007` |
| §136 | Notification deduplication | `OPS-012` |
| §137 | Notification acknowledgement | `OPS-013` |
| §138 | Promote-to-ROS contract | `AGT-061` |
| §139 | ROS state independence | `AGT-062` |
| §140 | Cross-application references | `AGT-045` |
| §141 | v1 elevations | `SCOPE-003` |
| §142 | Deferred from this pass | `SCOPE-021` |
| §143 | Guiding rule for new features | `GOV-021` |

## v0.5 — §144–§151

| § | Topic | Requirements |
|---|---|---|
| §144 | Required shared platform dependencies | `PLAT-001`–`PLAT-003` |
| §145 | Aegis installation and startup | `PLAT-010`–`PLAT-012` |
| §146 | Aegis use, fault ownership, and verification | `PLAT-020`–`PLAT-027` |
| §147 | Forma installation | `PLAT-030`, `PLAT-031` |
| §148 | Forma usage | `PLAT-032`–`PLAT-037` |
| §149 | Folio installation | `PLAT-040`, `PLAT-041` |
| §150 | Folio usage | `PLAT-042`–`PLAT-048` |
| §151 | Cross-platform composition and enforcement | `PLAT-050`–`PLAT-053` |

## Coverage

| | Count |
|---|---|
| Source sections | 151 (plus 11 `§47.x` subsections and 3 `§0.x` subsections) |
| Sections with no mapped requirement | **0** |
| Distinct requirements | 301 |

### By area

| Area | Requirements |
|---|---|
| `DOM` — domain model | 50 |
| `AGT` — agent and integration | 36 |
| `PER` — persistence | 34 |
| `UI` — user interface | 26 |
| `TIME` — time and scheduling | 22 |
| `GOV` — governance | 21 |
| `OPS` — operations | 20 |
| `SEC` — security | 20 |
| `QRY` — query and retrieval | 18 |
| `TST` — testing and documentation | 13 |
| `SCOPE` — scope | 6 |
| `PLAT` — shared platform dependencies | 35 |
| **Total** | **301** |

### By release

| Release | Meaning |
|---|---|
| `v1` | In the first implementation, including the §97 and §141 elevations. |
| `deferred` | Explicitly deferred by §98 or §142. |
| `future` | Described as eventual with no release assigned. |

## Requirements with no single source section

Two requirements consolidate statements scattered across many sections rather
than deriving from one:

| Requirement | Consolidates |
|---|---|
| [`SEC-005`](SECURITY.md#vig-sec-005) — token containment | §47.4, §66, §69, §70, §76, §86, §87, §118, §128, §134 |
| [`TST-023`](QUALITY.md#vig-tst-023) — documented behaviours | §61, §67, §70, §81, §86, §89, §113, §116, §118, §128 |

## Cross-system contract — `PROV`

The `PROV` area does not derive from a source section of the input documents.
It derives from the Praxis provenance contract
(kemiller2002/praxis@a42c44e8ae0e6e16fdd513141460b700e5fa6648) and is traced
from it here instead. It is not counted in the totals above.
[`PROVENANCE.md`](PROVENANCE.md#traceability) traces each requirement on to its
implementation and tests.

| Praxis record | Requirements |
|---|---|
| `DF-ROS-2026-A036`, `DF-ROS-2026-A037` | `PROV-001`–`PROV-017` |
| `RQ-ROS-2026-A001`, `A002` (actor, execution) | `PROV-001`, `PROV-003`, `PROV-015` |
| `RQ-ROS-2026-A004` (append-only contributions) | `PROV-002`, `PROV-005`, `PROV-011`, `PROV-016` |
| `RQ-ROS-2026-A007` (legacy compatibility) | `PROV-007`, `PROV-008` |
| `RQ-ROS-2026-A008` (lineage) | `PROV-010` |
| `RQ-ROS-2026-A009` (integration boundaries) | `PROV-002`, `PROV-009` |
| `RQ-ROS-2026-A010`, `A019` (not authority) | `PROV-014` |
| `RQ-ROS-2026-A013` (foreign execution keys) | `PROV-003`, `PROV-004` |
| `RQ-ROS-2026-A014` (role vocabulary) | `PROV-003` |
| `RQ-ROS-2026-A015` (receiving rules) | `PROV-001`, `PROV-006` |
| `RQ-ROS-2026-A016` (explicit propagation) | `PROV-009` |
| `RQ-ROS-2026-A017` (no credentials) | `PROV-013` |
| `RQ-ROS-2026-A018` (conformance) | `PROV-017` |

Existing requirements this area extends, without changing their text:
`DOM-035`, `DOM-037` (projection, `PROV-015`), `AGT-035` (envelope,
`PROV-009`, `PROV-011`), `AGT-045` (references, `PROV-010`), `PER-020`–`PER-023`
(versioning, `PROV-007`).
