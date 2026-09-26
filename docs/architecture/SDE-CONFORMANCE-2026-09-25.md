---
id: DF-SDE-CONFORMANCE-002
title: Conformance re-evaluation — Vigila on main at 287b082, measured against SDE 1.3.0
status: accepted
created: 2026-09-25
work_item: WI-0024
follows: DF-SDE-CONFORMANCE-001 (WI-0015)
sde_version: 1.3.0
scope: everything on `main` at 287b082 (27 commits after 52f28b7, PRs #10–#13 plus 23 direct commits)
---

# Vigila against SDE, one week later

This re-runs the [2026-09-18 evaluation](SDE-CONFORMANCE-2026-09-18.md)
(`WI-0015`, findings F-1 … F-11). ROS treats `complete` as a terminal state, so
WI-0015 could not be reopened; this work is a new item, `WI-0024`, and this
document does not replace the first one. It does three things:

1. re-tests every earlier finding against the current code;
2. applies what SDE 1.3.0 adds: `DECISION-AND-EVIDENCE-SEMANTICS.md` (new) and
   the Ordo/ROS guardrail in `FOUR-TIER-ARCHITECTURE.md` 0.2.1;
3. audits the 27 commits since then, which added the connection flow, Aegis at
   the Limen boundary, Forma/Folio composition and the Limen 0.6.2 upgrade.

Evidence classes are the ones the first document defined: `MECHANICAL`,
`ARTIFACT`, `SELF-REPORT`, `NOT CAPTURED`. This document adds one more,
`NOT RUN`, for a check that could not run in this environment. `NOT RUN` means
the check was not run here. It does not mean the check passed.

## Summary judgement

**The three high-value verification gaps are closed. The two structural
findings are unchanged, and the new code has made one of them worse.**

- F-4 (default serializer at the host boundary), F-5 (no browser proof) and
  F-6 (a grep standing in for an agreement test) are all **closed**, closed
  properly, and enforced in CI.
- F-1/F-2 (no semantic map or manifest), F-3 (no change classification) and
  F-8 (no completion report) are **still open**. Every record made since then
  has the same `"rationale": null`, apart from this one.
- F-7 (the Tier 2 project is composed by nothing) is **still open**. It now
  matters more: SDE 1.3.0 says explicitly that Tier 2 owns transitions *and the
  interpretation of effect results*. The connection flow added both, in
  Tier 3, next to an empty Tier 2 project that is still not composed (G-1).
- The 22 commits on `main` since 2026-09-21 carry **no ROS attribution**. They
  include the Aegis boundary guard, a feature commit. `./ros validate` still
  passes, because it checks only uncommitted (dirty) paths, not history (G-3).

## Earlier findings, re-tested

| Finding | 2026-09-18 | Now | Basis |
|---|---|---|---|
| F-1 No `SDE-MAP.md` | open | **open** | `ls SDE-MAP.md` → no such file. Filed as WI-0019, still `captured` |
| F-2 No manifest / `not needed` reason | open | **open** | no `manifest.md`, no `not needed` record. WI-0019 |
| F-3 No change site classified | open | **open** | every execution through WI-0023 has `"rationale": null`, `"evidence": []` |
| F-4 Default serializer at host boundary | high | **closed** | `Dispatch.writeResponse` writes through `Utf8JsonWriter` (`Dispatch.fs:783`). `Effect` is a closed DU with one case per Limen capability (`Effects.fs:48`) |
| F-5 No browser verification | high | **closed** | `tests/browser/{capture,connection}.spec.js`, 17 tests. CI `Build` runs `npx playwright test`; green on 287b082 |
| F-6 View-binding grep | moderate | **closed** | `scripts/check-view-bindings.sh` deleted, replaced by `tests/Vigila.Application.Tests/ViewBindingTests.fs` |
| F-7 Persistence built ahead of the slice | moderate | **open, now smaller in share** | 895 of 3,014 production lines (29.7%, was 45%) are still unreachable from the app. See G-1 |
| F-8 No completion report / execution log | moderate | **open** | the `.sde/templates/` completion-report and execution-log templates are still unused |
| F-9 Zero-second durations | moderate | **improved, not closed** | WI-0016: 716 s, WI-0023: 1,102 s. WI-0017, 0018, 0022: 0.5–1.1 s |
| F-10 `sde verify` scans build output | low | **open** | `dotnet.js` flagged `[strong-review]` whenever `src/Vigila.Wasm/bin` exists. No `sde.config.json`. Filed as WI-0020, still `captured` |
| F-11 Traceability totals off by one | low | **fixed then, recurred in another form** | see G-4 |

About F-9: WI-0017 and WI-0018 were shipped inside WI-0016 and closed
afterwards (#12). Their sub-second executions describe a bookkeeping step, not
the work, and the work itself is covered by WI-0016's 716 s. That is honest,
but only because PR #12 says so. The execution records themselves still read
as observations, so F-9 is improved, not closed.

## New findings

### G-1 — Connection transitions and effect interpretation live in Tier 3 · severity: moderate · REQUIRED (SDE 1.3.0)

`DECISION-AND-EVIDENCE-SEMANTICS.md` §1 is new in 1.3.0:

> Tier 2 owns pure decisions, guards, policy, transitions, and interpretation
> of effect results. Tier 3 coordinates requests/results and boundary translation.

The connection flow (WI-0023) puts all of that in `Vigila.Application`:

- `Dispatch.apply` (`Dispatch.fs:262`, ~160 lines) is the connection state
  machine: `Connect` → probe repository → probe branch → `Connected`, and
  `Disconnect`;
- `Connection.classifyStatus` and `classifyWriteCapability`
  (`Connection.fs:330`) turn HTTP results into domain refusals. The doctrine's
  name for that is "interpretation of effect results";
- `Dispatch.fs:188` still carries a section header, *"Transitions - pure, and
  the only place these decisions are made"*, with nothing under it.

`Vigila.Transition` (Tier 2, 63 lines) is still referenced by
`Vigila.Application.fsproj` and opened by no source file.

`scripts/check-architecture.sh` cannot see this, and was never designed to. It
checks *dependency direction*, which is intact. This finding is about
*responsibility placement*, which a dependency check cannot detect. The
architecture check's "OK" should not be read as covering it.

Mitigating, and recorded as such: the logic is pure, well tested
(`ConnectionTests.fs`, 377 lines; browser `connection.spec.js`), and handles
`Unknown` correctly (below). The finding is about where it lives, not whether
it works. Moving it is an edit today. Once item transitions also land in
Tier 3, it becomes a migration.

Evidence class: `MECHANICAL` (`grep -rn "Vigila.Transition" src` finds only
two doc comments in Tier 1), `ARTIFACT`.

### G-2 — The Aegis boundary guard bypasses the Aegis port · severity: low

`VIG-SHR-010` (Aegis at operational boundaries) is met. `Dispatch.handle`
wraps `step` in `Aegis.capture`, JSON faults are classified as
`VIGILA.BOUNDARY.MESSAGE_INVALID`, and domain refusals stay typed outcomes,
not faults. That is correct.

But it calls `Aegis.scope` directly (`Dispatch.fs:835`), while
`Ports.scopeFor`, documented as *the* place scopes are built so that context
passes through redaction (VIG-SEC-005, VIG-PER-032), still has no caller
anywhere, in source or tests. The same capability now has two paths, and the
one that is documented is the one that is unused. The call site passes
`Map.empty`, so nothing leaks today. The risk is the next call site copying
this one.

Evidence class: `MECHANICAL` (`grep -rn scopeFor src tests`).

### G-3 — 22 commits on `main` carry no ROS attribution · severity: moderate · process

Every commit from `f2273fa` (2026-09-23) to `287b082` (2026-09-25) touches
no `.ros/` path, and no work item names them. They include
`feat: guard Vigila Limen boundary with Aegis`,
`feat: compose Vigila with Forma and Folio`,
`feat: register Folio document primitives`, the Limen 0.6.2 upgrade and three
requirements changes. None came through a PR, and two of them
(`8525cbd`, `35ea26b`) left `Build` red on `main` until `287b082`.

`AGENTS.md` requires meaningful committed changes to carry machine-readable
attribution. `./ros validate` passes because it only guards dirty paths.
Committed history is outside what it checks. So "validation passed" does not
mean "the history is attributed", and nothing in CI connects the two.

A related artifact: `ECHELON-UPGRADE-2026-09-21` set the repository-level
`actor` in `.ros/context/current.json` to `agent:chatgpt`. That label now
appears on every later `work context` output, including this Claude Code
session's. Each execution's own `identity` block is correct
(`provider: anthropic, runtime: claude-code`), so this is a display
inconsistency rather than a misattribution, but the two disagree.

Evidence class: `MECHANICAL` (`git show --stat` per commit), `ARTIFACT` (CI
runs 66–68 on `main`).

### G-4 — Seven normative requirements are outside the traceability count · severity: low

`SHARED-CAPABILITIES.md` holds seven requirements (`VIG-SHR-001` … `-040`)
that `README.md:137` calls normative across the whole set. They use `##`
headings where every other requirement uses `####`, so the documented
mechanical count (`^#### VIG-`) skips them. `TRACEABILITY.md` has no `SHR`
row, and its total of 301 matches the `####` count exactly. The totals are
internally consistent and still leave out seven normative requirements.

This is F-11 again, in a different shape: a derived summary drifting from its
source. This time the cause is a heading convention, not an arithmetic slip.

Evidence class: `MECHANICAL`.

### G-5 — A comment contradicts the code it documents · severity: low

`Effects.fs:45`: *"Vigila requests none of them yet."* Since WI-0023 the engine
issues `Http` effects (`Dispatch.fs:212`, `232`, `396`). Low severity, but it
is the doc-drift rule from `FEATURE-MANIFESTS.md`: when the authority
changes, the text pointing at it changes in the same work item.

Evidence class: `MECHANICAL`.

### G-6 — `Dispatch.fs` has entered the review band · observation

`sde verify`: `Dispatch.fs` 854 physical lines (was below 500). It holds four
clusters: state and commands, `apply` (transitions, see G-1), `project`
(view), and the hand-written wire reader and writer (557–820). Under
`STRUCTURAL-LOCALITY.md` this is a review prompt, not nonconformance. G-1
decides the natural split: moving `apply` to Tier 2 removes the largest
cluster. The wire codec is the next candidate, as `Codec.fs`.

Evidence class: `MECHANICAL`.

## What SDE 1.3.0 adds that is already conformant

- **Unknown is not Failed (§5).** `HttpOutcome` = `Responded | Unreachable |
  Unknown` (`Dispatch.fs:166`). Limen's `OutcomeUnknown` maps to `Unknown`
  and is not folded into a failure, citing VIG-UI-014. The only effects are
  read-only probes, so the reconciliation obligation does not arise yet. It
  will arise with the first GitHub write, and the type is ready for it.
- **Capability is not authentication (§6).** The token never enters the
  engine. The kernel reports only *whether* one was typed (`tokenEntered`),
  and `HttpEffect` carries no credential (`Effects.fs:13`).
- **ROS is not Tier 5 (tier guardrail).** Nothing under `src/` references ROS,
  and application behaviour does not depend on it.
- §2–§4 and §7 (state views, coverage claims, derived-evidence closure,
  negative knowledge) govern Ordo bounded decisions. Vigila makes none, so
  they do not apply yet. They are recorded here as not applicable, not as
  conformant.

## Metrics

| Metric | Value | Evidence class |
|---|---|---|
| Agent / provider / runtime | Claude Code, anthropic, session `c3aa91ca` | `ARTIFACT` (execution identity block) |
| Model | `null` in the ROS record | `NOT CAPTURED` |
| Production F# / C# lines | 3,014 (was 1,993) | `MECHANICAL` |
| Unreachable from the running app | 895 (29.7%) — `Vigila.Transition` 63 + `Vigila.Host.GitHub` 832 | `MECHANICAL` |
| .NET tests | 145 passing, 0 skipped (Semantic 15, Transition 8, Application 53, Host.GitHub 69) | `MECHANICAL` — `dotnet test Vigila.sln -c Release`, this session |
| Tests covering uncomposed code | 77 of 145 (53%) | `MECHANICAL` |
| Browser tests | 17 (capture 6, connection 11) | `NOT RUN` here; `ARTIFACT` green in CI `Build` #68 on 287b082 |
| Architecture check | OK | `MECHANICAL` — `./scripts/check-architecture.sh` |
| `sde verify` | passes; 4 review warnings (1 is build output) | `MECHANICAL` |
| `ros validate` | passed | `MECHANICAL` — see G-3 for what it does not cover |
| Commits since 52f28b7 with no ROS attribution | 22 of 27 | `MECHANICAL` |
| Tokens, cost, turns | — | `NOT CAPTURED`: no hook, status-line or OTel stream is connected in this environment |

### Why the browser suite was not run here

`npm ci` fails in this environment: the Folio dependency is fetched as a
GitHub archive (`package.json`: `kemiller2002/folio/archive/…tar.gz`), and the
session's network proxy refuses that host with 403. The Playwright result
above is CI's, on the same commit, and is reported as an artifact, not as a
run from this session. Vendoring Folio through a release asset or a registry
would make the suite reproducible in restricted environments. That is a
recommendation, not a finding.

### This work item's own record

As with WI-0015: this is analysis, and ROS requires `implementation` and
`tests` evidence. Both paths point at this document. Unlike WI-0015, this
execution was opened before the work began and has a classification
rationale. Its duration is therefore an observation of the audit, not of the
attribution step (F-9).

## Recommended next actions, in priority order

1. **G-1: decide where the connection transitions belong.** Either move
   `apply`'s connection arms and the `classify*` interpreters into
   `Vigila.Transition`, which composes Tier 2 for the first time and removes
   most of F-7's Tier 2 share, or record a deviation outside `.sde/` saying why
   they stay in Tier 3. Do it before item transitions are written, which will
   otherwise follow the same pattern.
2. **G-3: attribute or declare the unattributed 22 commits**, and make PRs the
   path to `main`: two of those direct pushes broke `Build` there.
3. **F-1/F-2 (WI-0019)** are still the cheapest open items, and G-1 shows the
   cost of not having them: a routing table naming Tier 2 as the owner of
   transitions would have put the connection state machine there.
4. **G-2**: route the boundary scope through `Ports.scopeFor`, or delete
   `scopeFor` and document `Dispatch.handle` as the one place scopes are built.
5. **F-10 (WI-0020)**, **G-4**, **G-5**: small edits.

G-1, G-2, G-3 and G-4 are filed as `WI-0025` … `WI-0028`. G-5 is a one-line
change and belongs to whichever item next touches `Effects.fs`.
