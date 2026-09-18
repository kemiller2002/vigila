---
id: DF-SDE-CONFORMANCE-001
title: Conformance evaluation — how Vigila was built, measured against SDE 1.2.0
status: accepted
created: 2026-09-18
work_item: WI-0015
sde_version: 1.2.0
scope: everything on `main` at 52f28b7 (PRs #1–#9)
---

# How Vigila was built, measured against SDE

This is a self-assessment of the work in PRs #1–#9 against the method installed
under [`.sde/`](../../.sde/). It is written to be usable as a defect record, so
it names what was not done and what that cost, not only what was.

SDE says its files are versioned methodology inputs and that a project recording
a deviation must do so outside `.sde/`. This document is that record.

## How to read the evidence classes

[`ENGINEERING-METRICS.md`](../../.sde/reference/ENGINEERING-METRICS.md) requires
every reported figure to carry its evidence class. Applied here:

| Class | Meaning in this document |
|---|---|
| `MECHANICAL` | Reproduced by a command in this repository; the command is given. |
| `ARTIFACT` | Read directly out of a committed file or a `.ros/` record. |
| `SELF-REPORT` | The executing agent's own account of the session. Not independently observable. |
| `NOT CAPTURED` | Genuinely not recorded. Not normalised to zero. |

## Summary judgement

**SDE's architecture doctrines were followed closely and, unusually, enforced
mechanically rather than asserted. SDE's method — the routing, classification,
and recording contracts — was largely not followed.**

The four-tier layout, boundary preservation at the persistence edge, single
semantic authority, and illegal-states-unrepresentable modelling are all present
and all have a check that fails the build when they are violated. Against that:
no repository semantic map was created, no feature manifest was created and no
`not needed` rationale was recorded, no change site was classified, no completion
report or execution log was written, and the composed browser path has no
behavioural verification at all.

The architecture held anyway. That is worth stating carefully rather than
triumphantly: the whole repository is 1,993 lines of production F#, which fits in
one agent's working context, so the navigation contracts SDE prescribes had
nothing to bound. **The parts of SDE that were skipped are precisely the parts
whose value a project this small cannot demonstrate.** Their cost lands on the
next session, not this one.

## Conformance by document

| SDE document | Verdict | Basis |
|---|---|---|
| `architecture/FOUR-TIER-ARCHITECTURE.md` | **Conformant, enforced** | `scripts/check-architecture.sh`, run before compile in CI |
| `architecture/STRUCTURAL-LOCALITY.md` | **Largely conformant** | one file in the review band; single authority preserved; see F-7 |
| `architecture/BOUNDARY-PRESERVATION.md` | **Split** — conformant at persistence, non-conformant at the host boundary | F-4 |
| `method/CONSTRUCTION-METHOD.md` | **Partially followed** | greenfield ordering inverted; stop conditions mostly met; F-5, F-7 |
| `method/CHANGE-CLASSIFICATION.md` | **Not followed** | F-3 |
| `method/NAVIGATION-AND-CONTEXT.md` | **Not applicable as executed** | no declared context existed to navigate; F-1, F-2 |
| `method/FEATURE-MANIFESTS.md` | **Not followed** | F-1, F-2 |
| `method/AGENT-EXECUTION-RULES.md` | **Partially followed** | rules 9–13 yes; rules 1–8, 14–15 no |
| `method/VERIFICATION-METHOD.md` | **Partially followed** | strong up to behavioural; integration and live verification absent; F-5 |
| `reference/ENGINEERING-METRICS.md` | **Partially followed** | correct telemetry authority, untruthful durations; F-8, F-9 |

`.sde/README.md` prescribes six steps before engineering a change. Steps 5
(apply the architecture documents) and 6 (verification method) were followed.
Steps 1–4 — identify the semantic feature, read the map and manifest, read the
construction method and classify the work, follow the navigation and agent-
execution contracts — were not. **Four of six entry steps were skipped**, and
this is the single largest finding in this document.

---

## What was followed, with evidence

### Four-tier architecture, enforced rather than asserted

Tier 1 (`Vigila.Semantic`) and Tier 2 (`Vigila.Transition`) carry no
`PackageReference` and no `ProjectReference` beyond `FSharp.Core`, and are
forbidden from naming `Aegis`, `System.Net`, `System.IO`, `HttpClient`,
`Octokit`, `localStorage`, or `System.Text.Json`. This is not a convention
someone is asked to respect; `scripts/check-architecture.sh` fails the build,
and CI runs it **before** `dotnet restore` so a violation fails fast rather than
after a successful compile.

```
$ ./scripts/check-architecture.sh
  OK    dependencies point downward only; Tier 1 and Tier 2 are clean.
```

Evidence class: `MECHANICAL`.

Aegis enters at `Vigila.Application/Ports.fs` and no lower, with the reason
written into the project file: a transition refusal is a domain answer, not a
fault, so the fault-handling library has no business in the tier that decides
transitions.

### Single semantic authority

[`STRUCTURAL-LOCALITY.md`](../../.sde/architecture/STRUCTURAL-LOCALITY.md) makes
this REQUIRED: physical decomposition organises responsibility, it does not
multiply authority. Three places where the rule was actually load-bearing:

- `Dispatch.apply` does not re-implement "a title is required". It calls
  `Title.create` — Tier 1 — and renders the refusal. Had the engine decided this
  itself, the rule would exist twice and could disagree.
- `GitHubStore.codeOf` is deliberately separate from the DU case names, so
  renaming a case cannot silently change the wire contract.
- Every mutation of `Item` passes through one private `record` function, so
  history cannot be written by one path and skipped by another.

Evidence class: `ARTIFACT`.

### Preserve closure and delay weakening

[`BOUNDARY-PRESERVATION.md`](../../.sde/architecture/BOUNDARY-PRESERVATION.md)'s
REQUIRED principles are visible in the model rather than in a comment about the
model:

- `WhenValue` distinguishes `OnDate of DateOnly` from `AtInstant of Instant`, and
  the persisted form tags them (`{"kind":"date","value":"YYYY-MM-DD"}`), so a
  reader cannot mistake a calendar date for a moment and shift it a day.
- `ItemId`, `NoteId`, `WorkspaceId`, `Title`, `Tag`, and `StoragePath` all have
  private constructors. An invalid one is unrepresentable rather than validated
  repeatedly at each use.
- `ItemJson` rejects unknown values in a *closed* set (a status, a kind) while
  ignoring unknown *fields*. That asymmetry is the correct one: forward
  compatibility for additive change, refusal to fabricate domain state.

F# gives hard compile-time exhaustiveness here, so the enforcement strength is
real and not warning-only. `BOUNDARY-PRESERVATION.md` requires that distinction
to be stated rather than glossed, so it is stated.

### Earliest trustworthy mechanism, and behavioural proof that actually found things

The CI ordering matches the failure-detection order in
[`VERIFICATION-METHOD.md`](../../.sde/method/VERIFICATION-METHOD.md): architecture
check → boundary check (`limen verify --strict`) → compile with warnings as
errors → tests → publish.

Two `StorageLayout` defects were found by writing traversal tests before the
implementation was accepted, not by review: `//server/share` was silently
accepted as the relative path `server/share`, and an all-whitespace path
normalised to empty. Both are now refused.

One mutation-testing run was discovered to have silently not applied its
replacement — the target text had drifted, so the run "passed" while proving
nothing. It was re-run with an assertion that the target exists; the second
attempt failed 8 tests as intended. **A check that proved nothing was not
counted as a check that passed**, which is the `AGENT-EXECUTION-RULES` honesty
rule applied to a case where the dishonest reading was available and easier.

```
$ dotnet test Vigila.sln --configuration Release
Passed: 8 (Transition) + 15 (Semantic) + 15 (Application) + 69 (Host.GitHub) = 107
```

Evidence class: `MECHANICAL`.

### The correct telemetry authority

[`ENGINEERING-METRICS.md`](../../.sde/reference/ENGINEERING-METRICS.md) REQUIRES
reusing an adopting project's existing collector rather than building a parallel
one. ROS is that collector and was used for all 14 work items. ROS also gets the
hard part right on its own account: unavailable metrics are recorded as
`"status": "supported-unavailable"` with a reason, never as zero.

This is conformance inherited from the tooling rather than earned by the agent,
and it is recorded as such.

---

## Findings

### F-1 — No repository semantic map · severity: moderate · REQUIRED

[`FEATURE-MANIFESTS.md`](../../.sde/method/FEATURE-MANIFESTS.md) states that a
nontrivial adopting repository MUST expose a small top-level routing map, default
name `SDE-MAP.md`. None exists, and no existing document was nominated as one.

```
$ ls SDE-MAP.md
ls: cannot access 'SDE-MAP.md': No such file or directory
```

Routing was carried in the conversation instead, which does not survive the
session. The concrete cost is visible in F-7: `Vigila.Transition` exists,
compiles, is tested, and is composed by nothing. A routing table would have
exposed that on its first read rather than on this audit.

SDE labels the *cost-reduction* claim for maps and manifests EXPERIMENTAL
(`HY-SDE-2026-0008`). So this is nonconformance with a REQUIRED routing contract,
not the violation of an established result. Stated precisely rather than
inflated.

Evidence class: `MECHANICAL`.

### F-2 — No feature manifest and no recorded `not needed` rationale · severity: moderate · REQUIRED

`FEATURE-MANIFESTS.md` explicitly permits a small repository to use the map alone
— but requires recording the manifest as `not needed` with a short reason "rather
than creating ceremony". Neither a manifest nor a rationale exists. Vigila may
well be small enough that the answer is legitimately `not needed`; that answer
was never written down, so the absence is indistinguishable from an omission.

```
$ find . -name manifest.md -not -path './node_modules/*'
(no output)
```

Evidence class: `MECHANICAL`.

### F-3 — No change site was classified · severity: moderate · REQUIRED

[`CHANGE-CLASSIFICATION.md`](../../.sde/method/CHANGE-CLASSIFICATION.md) requires
each consequential change site to be classified as Semantic, Boundary, Mechanical
Propagation, or Presentation before or as it enters the Construction Method, and
warns explicitly against "assigning one ceremonious label to all work".

All 14 work items carry ROS's own vocabulary (`mechanical` or `task`) and nothing
else. Every telemetry record has `"rationale": null` and `"evidence": []`.

```
$ ./ros status | python3 -c "..."
WI-0001 mechanical ... WI-0014 task      # ROS types, not SDE change classes
```

The cost is not abstract. The Limen wiring (WI-0014) and the storage layout
(WI-0007) are **Boundary Changes**, whose prescribed path is "explicit boundary
decision → contract verification → **host/integration proof**". Because no site
was classified as a Boundary Change, that third step was never scheduled — which
is exactly F-5. The classification step is the thing that would have put the
missing verification on the list.

Evidence class: `ARTIFACT`.

### F-4 — The host boundary uses a default serializer · severity: high · REQUIRED

`BOUNDARY-PRESERVATION.md`'s Three Contract Model states, for the **Host
Contract** — which is precisely the WASM-engine-to-browser-kernel relationship
Limen creates:

> every boundary-crossing value gets an explicit, hand-written tagged-JSON
> rendering function, never a host language's or framework's default serializer

`Dispatch.writeResponse` does the opposite:

```fsharp
let private writeResponse state =
    let payload =
        {| view = project state
           effects = ([]: obj list)
           cancellations = ([]: string list) |}
    JsonSerializer.Serialize(payload, options)
```

`System.Text.Json`'s reflection over an F# anonymous record decides the wire
shape. The field names on the wire are whatever the compiler emitted; nothing
pins them.

Two aggravating details:

1. **The same rule was applied correctly one tier away.** `ItemJson.fs` writes
   every field by hand through `Utf8JsonWriter` with explicit tags. The doctrine
   was known and was applied at the persistence boundary and not at the host
   boundary. This is not ignorance of the rule; it is inconsistent application of
   it.
2. `effects = ([]: obj list)` is an open type where the doctrine requires
   "effects described by a closed algebra, one constructor per legal operation —
   not a flat record with optional/nullable fields". This is textbook
   **Representation Collapse**, currently latent only because the list is always
   empty. It will stop being latent the moment the GitHub effects land, which is
   the next planned work.

Evidence class: `ARTIFACT`.

### F-5 — The composed browser path has no behavioural verification · severity: high · REQUIRED

`VERIFICATION-METHOD.md` calls the behavioural/integration row "the load-bearing
exception to 'the compiler can catch everything'" and marks it **REQUIRED** — not
recommended — for any dispatch arm whose body is not itself type-checked against
its semantic input. `AGENT-EXECUTION-RULES.md` repeats it: never treat a green
compiler/architecture/contract run as proof of behavioural correctness for a
dispatch arm, because it has a 100% miss rate across two independent experiments.

What is verified: the 15 `Vigila.Application.Tests` drive real
browser-to-engine JSON through `step` and `handle` and assert on the emitted
JSON. That is genuine behavioural proof — of the engine.

What is not verified, by anything: `Program.cs`'s `[JSExport] Dispatch`
marshalling shim, `wasm-engine-transport.js`, the `BrowserKernel` wiring in
`main.js`, and whether `web/index.html` actually renders a captured item. CI's
final step runs `npm run build:wasm`, which proves the engine **publishes** to
WebAssembly. It does not prove the page works. **No browser has ever loaded this
application**, in CI or locally.

This is the highest-value gap in the repository and it is cheap to close:
Chromium and Playwright are already available in this environment.

Evidence class: `MECHANICAL` (absence of any such test), `SELF-REPORT` (that the
page was never loaded).

### F-6 — The view-binding check is a text-presence proxy, not an agreement check · severity: moderate

`scripts/check-view-bindings.sh` was written by this agent, and the CI workflow
describes it as turning a silent browser failure into a build failure. It is
weaker than that claim. It greps for the binding key *anywhere* in `Dispatch.fs`,
including comments:

```
$ for key in browser engine Limen title; do
    grep -qE "(^|[^A-Za-z])$key( |=)" src/Vigila.Application/Dispatch.fs && echo "PASSES: $key"
  done
PASSES: browser
PASSES: engine
PASSES: Limen
PASSES: title
```

`data-text="browser"` would pass the check and render nothing, because "browser"
appears in a prose comment. The check guards only bindings whose key never
coincides with English prose in that file — a property no one is maintaining.

`BOUNDARY-PRESERVATION.md` asks for agreement "checked by an automated test, not
discovered live". The honest form is a test that serialises a projection and
asserts every `data-*` key in the HTML is a key in the emitted JSON. That is the
same cost and an actual guarantee.

**A check that overstates what it proves is worse than no check**, because it is
cited as coverage. This one was.

Evidence class: `MECHANICAL`.

### F-7 — The persistence boundary was built ahead of the vertical slice · severity: moderate

`CONSTRUCTION-METHOD.md`'s greenfield workflow orders the work:

> … → build the smallest meaningful vertical semantic slice → **introduce only
> required host and persistence boundaries** → verify end-to-end behavior →
> expand capability by capability

with the explicit caution: "Do not use SDE as permission for unlimited up-front
modeling."

The order actually executed was: four-tier scaffold (PR #3) → **storage layout
and the complete persisted form** (PR #4) → capture slice (PR #6) → browser
wiring (PR #9). The persistence boundary landed two PRs before the first vertical
behaviour and serialises notes, history, sources, snooze, tags, and resolution —
none of which any vertical slice produces.

Measured consequence:

| | Lines | Composed into the running app? |
|---|---:|---|
| `Vigila.Semantic` | 796 | yes |
| `Vigila.Application` | 283 | yes (`Ports.fs` compiled, `scopeFor` uncalled) |
| `Vigila.Wasm/Program.cs` | 19 | yes |
| `Vigila.Transition` | 63 | **no** |
| `Vigila.Host.GitHub` | 832 | **no** |

`Vigila.Wasm.csproj` references `Vigila.Application` and nothing else, so
**895 of 1,993 production lines (45%) are unreachable from the running
application.** `ItemStore` is declared in `Ports.fs` and implemented by nothing.
`scopeFor` — the single point where Aegis integrates — has no caller anywhere,
including tests. `Dispatch.fs` never opens `Vigila.Transition`, so the running
application performs no transition.

The test distribution follows the same inversion: **69 of 107 tests (64%) cover
the tier nothing composes; 15 cover the engine that actually runs.**

Mitigating, and recorded as such: the storage layout was user-directed — OQ-08
was raised to the user as a decision needing an answer and the user asked for it
to be implemented. This was not unilateral over-modelling. But SDE's ordering
advice was not raised against the request either, and it should have been, as a
sentence of the form "this is the persistence boundary before the vertical slice;
here is what that costs."

Evidence class: `MECHANICAL` (line counts, project references), `ARTIFACT`
(PR order).

### F-8 — No completion report and no execution log · severity: moderate

`.sde/templates/completion-report.md` and `execution-log.md` exist and were used
zero times. ROS records attribution, state transitions, and capability discovery;
it does not carry the SDE completion-report fields. Specifically **not recorded
anywhere durable**, for any of the 14 work items:

- change classes and why;
- semantic feature and manifest;
- declared modification boundary;
- undeclared dependencies discovered during work;
- cross-boundary edits and their justification;
- skipped or unavailable checks and their implications.

That last one matters most. `CONSTRUCTION-METHOD.md`'s stop conditions require
that skipped checks be "named with their implications". F-5 is a skipped check
that was never named — it is being named for the first time in this document,
after the work shipped.

Evidence class: `MECHANICAL`.

### F-9 — ROS execution durations are not a truthful record of the work · severity: moderate · Methodology defect

Eight of fourteen executions finalise in 0 seconds:

```
WI-0001 4s    WI-0006 292s   WI-0011 0s
WI-0002 0s    WI-0007 207s   WI-0012 188s
WI-0003 641s  WI-0008 0s     WI-0013 0s
WI-0004 0s    WI-0009 0s     WI-0014 0s
WI-0005 0s    WI-0010 42s
```

WI-0013 and WI-0014 produced the Limen engine and the web UI — hours of work
recorded as zero. The execution record was opened and closed *around the
attribution*, not around the work.

`ENGINEERING-METRICS.md` is explicit that "a missing value must never be
normalized to zero" and that each metric must preserve its completeness window.
A 0-second duration presented as an observation is exactly that normalisation.
The correct value for those executions is `NOT CAPTURED`.

This is a **Methodology** defect in the `CHANGE-CLASSIFICATION.md` defect
taxonomy — the agent's use of the collector, not a defect in ROS, which recorded
faithfully what it was told.

Evidence class: `ARTIFACT`.

### F-10 — `sde verify` scans build output · severity: low

```
$ npx @echelon-foundry/sde verify
SDE v1.2.0 verified.
Structural review warnings (3):
  SDE-STRUCT-001 [strong-review] src/Vigila.Wasm/bin/Release/net10.0/wwwroot/_framework/dotnet.js: 1287 lines
  SDE-STRUCT-001 [review] src/Vigila.Host.GitHub/ItemJson.fs: 601 lines
  SDE-STRUCT-001 [review] tests/Vigila.Host.GitHub.Tests/Tests.fs: 522 lines
```

The first is generated third-party runtime code and is noise. SDE says to use a
project-root `sde.config.json` when the default source extensions do not fit the
project; none exists, so the two genuine findings sit beside a false one.

The two genuine findings are both in the 500–999 "review emerging responsibility
clusters" band, which under `STRUCTURAL-LOCALITY.md` is a review prompt and not
nonconformance. `ItemJson.fs` at 601 lines does hold two responsibility clusters
(writing and parsing) and is a reasonable split candidate — but per F-7 it is
also 601 lines of code nothing composes, so splitting it is not the first thing
to do with it.

Evidence class: `MECHANICAL`.

### F-11 — The traceability totals were off by one · severity: low · found by this audit

Mechanically counting `^#### VIG-` headings gives 266 requirements; `TRACEABILITY.md`
claimed 265, with `UI` listed as 25 against an actual 26. The missing one is a
requirement added while resolving an open question (`VIG-UI-004a` or
`VIG-UI-006a`) without the summary table being updated in the same change.

```
$ for f in docs/requirements/*.md; do grep -cE "^#### VIG-" "$f"; done | paste -sd+ | bc
267   # 266 requirements + 1 format example inside a fenced block in README.md
```

Corrected in this work item. The general lesson is the same one
`FEATURE-MANIFESTS.md` states as a drift rule: when the authority changes, the
document pointing at it is updated **in the same work item**, not later. A
derived count is a second representation of a fact, and it drifted exactly the
way an uncoordinated duplicate does.

Evidence class: `MECHANICAL`.

---

## Metrics

Reported per the evidence-class rule. Values that were not captured are named as
such and are **not** normalised to zero.

| Metric | Value | Evidence class |
|---|---|---|
| Agent / provider / runtime | Claude Code, anthropic, session `b9dc1828` | `ARTIFACT` (ROS identity block) |
| Model | `null` in every ROS record | `NOT CAPTURED` |
| Work items | 14 complete + WI-0015 (this) | `ARTIFACT` |
| Wall-clock per work item | 8 of 14 recorded as 0 s | `NOT CAPTURED` (see F-9) |
| Production F# / C# lines | 1,993 | `MECHANICAL` |
| Production lines unreachable from the app | 895 (45%) | `MECHANICAL` |
| Tests | 107 passing, 0 skipped | `MECHANICAL` |
| Tests covering uncomposed code | 69 (64%) | `MECHANICAL` |
| Requirements derived | 266 across 11 requirement documents | `MECHANICAL` |
| Open questions resolved | 11 of 11 | `ARTIFACT` |
| Build / test attempts and failures | — | `NOT CAPTURED` |
| Search operations, repair loops, files read before first edit | — | `NOT CAPTURED` |
| Tokens, cost | — | `NOT OBSERVABLE` from inside the session |

### One disclosure about this work item's own record

ROS requires `implementation` and `tests` evidence to complete a work item. This
one produced a document and no tests, so both evidence paths point at this file.
The `tests` entry in `WI-0015`'s ROS record therefore names a document, not a
test run. Recorded here rather than left to be inferred, because F-9 is a finding
about exactly this class of quiet normalisation. The underlying gap is that ROS's
required-evidence vocabulary has no category for analysis work.

Context Surface, CER, and Discovery Expansion are **not** reported. SDE requires
that the counting unit and declared baseline be fixed *before* execution; neither
was, and reconstructing a denominator afterwards is the specific thing
`NAVIGATION-AND-CONTEXT.md` forbids.

## Recommended next actions, in priority order

1. **Close F-5.** Add a Playwright test that loads `web/index.html` against the
   published WASM engine, types a title, captures, and asserts the item renders.
   Chromium is already available. This is the REQUIRED check with the documented
   100% miss rate for every other mechanism, and it is currently absent.
2. **Close F-4** before the GitHub effects land, not after. Replace
   `JsonSerializer.Serialize` in `writeResponse` with an explicit
   `Utf8JsonWriter` rendering, and give `effects` a closed DU with one case per
   legal effect. Doing this after effects exist is a migration; doing it now is
   an edit.
3. **Close F-6.** Replace the grep in `check-view-bindings.sh` with a test that
   serialises a projection and asserts key presence in the emitted JSON.
4. **Write `SDE-MAP.md` (F-1, F-2).** Five semantic areas, where each lives,
   and — for each — either a manifest or a recorded `not needed` reason. This is
   the cheapest finding to close and the one that compounds.
5. **Adopt the completion-report template for the next work item (F-8)**, and
   open the ROS execution *before* the work rather than around the attribution
   (F-9).
6. **Add `sde.config.json`** excluding `bin/` and `obj/` (F-10).
7. **Decide what `Vigila.Host.GitHub` is for (F-7).** It is correct, well
   tested, and composed by nothing. Either wire it behind `ItemStore` in the next
   slice or record explicitly that it is a staged boundary awaiting the
   connection flow. Leaving 45% of the production code in an undeclared state is
   the finding; deleting it is not the proposed remedy.

Items 1–4 and 6 are filed as `WI-0016` … `WI-0020`. F-11 was corrected in this
work item.

## A note on what this evaluation does not establish

SDE labels most of its own outcome claims EXPERIMENTAL. This document does not
show that following the skipped steps would have produced better code here; a
single project with no control condition cannot show that. What it shows is
narrower and still worth acting on: **four of six prescribed entry steps were
skipped, and two verification gaps (F-4, F-5) trace directly to the skipped
classification step rather than to a judgement that they were unnecessary.** The
method would have caught them. Nothing else did, until this audit.
