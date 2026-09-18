---
id: REQ-OQ
title: Open questions from requirements derivation
status: resolved
created: 2026-09-18
updated: 2026-09-18
---

# Open questions

Points where deriving the requirements needed a judgement the source documents
do not settle. Each records what the sources said, what was decided, and why.

**All eleven are resolved.** The file is kept rather than deleted: each entry is
the reasoning behind a decision that is now load-bearing elsewhere, and the
alternatives that were rejected are the part hardest to reconstruct later.

Two of them record deliberate deviations from the source documents, taken with
reasons rather than silently: [OQ-09](#oq-09) drops two resolution
classifications §105 lists, and [OQ-01](#oq-01) keeps §85's withdrawal of the
`Reminder` kind over §54's scope list.

A recurring argument decided three of these. Where a choice was close,
it went in the **reversible direction**: adding a case to a closed set later is
additive, while removing one after items carry it is a migration. That reasoning
settled [OQ-01](#oq-01), [OQ-09](#oq-09), and the shape of
[OQ-04](#oq-04).


---

<a id="oq-01"></a>

## OQ-01 — Does the `Reminder` kind survive? — **RESOLVED**

**Resolved 2026-09-18.** No. The kinds stay `Task`, `FollowUp`, `Waiting`.

**Was.** v0.1 §2 and v0.2 §2.4 require a `Reminder` kind and v0.2 §54 lists it in
v1 scope, while v0.3 §85 withdraws it as behaviour mistaken for a kind. Later
document governs, but §85 is phrased as a recommendation, so the conflict was
left open.

**Why withdrawn stands.**

1. §85's reasoning holds against the rest of the model. "Submit passport
   documentation" is a `Task`; "remind me Thursday" is a follow-up date or a
   snooze attached to it. The model already expresses reminder behaviour with
   [VIG-TIME-004](TIME.md#vig-time-004) and
   [VIG-TIME-010](TIME.md#vig-time-010), so a fourth kind would add a second way
   to say the same thing.
2. **The asymmetry decides it.** Adding a case to a closed set later is
   additive: new kind, new persisted value, older readers reject it only if the
   schema says to ([VIG-PER-022](PERSISTENCE.md#vig-per-022)). Removing one
   later is a migration over existing items
   ([VIG-PER-023](PERSISTENCE.md#vig-per-023)). Staying at three keeps the cheap
   direction open; starting at four does not.
3. §85's own condition is met — no accepted domain work conflicts, because the
   three-kind enumeration is what `src/Vigila.Semantic/Items.fs` already
   implements.

**Reversible.** If a real workflow shows a distinct semantic need, adding
`Reminder` is one DU case and one persisted value. §85 asks for exactly that
evidence before adding it.


---

<a id="oq-02"></a>

## OQ-02 — `Waiting` is both a kind and a state — **RESOLVED**

**Resolved 2026-09-18.** Both survive. A `Waiting` kind and a `Waiting` status
are independent: a `Task` may be in status `Waiting`, and the kind is a capture
convenience, not a claim about state.

**Was.** v0.2 §2.3 defines `Waiting` as an item *kind*; v0.2 §3 defines
`Waiting` as an item *status*. Neither says how they relate. Two other readings
were possible — that the kind implies the status, or that the kind should be
withdrawn the way §85 withdrew `Reminder`.

**Applied.** [VIG-DOM-005](DOMAIN.md#vig-dom-005) and
[VIG-DOM-008](DOMAIN.md#vig-dom-008) stay orthogonal, which is what
`src/Vigila.Semantic/Items.fs` already implements. In particular
`SetKind(Waiting)` does **not** imply a status transition, which would have
violated [VIG-AGT-020](AGENT.md#vig-agt-020) (commands do only what they say).

**Consequence.** The name is ambiguous at any call site that has both types in
scope, so one must be qualified — `ItemKind.Waiting` or `ItemStatus.Waiting`.
This is a real cost of the decision and it already broke a build during the
scaffold work. It is left visible rather than worked around: the compiler flags
every ambiguous use, which is the safest place for the ambiguity to surface.


---

<a id="oq-03"></a>

## OQ-03 — "Vigila" or "Vigilia"? — **RESOLVED**

**Resolved 2026-09-18.** The product is **Vigila**. "Vigilia" was a misspelling,
introduced into the repository name and propagated from there by `ros init`,
which derives the project name from the directory it runs in.

**Was.** All four source documents spell the product Vigila, while the
repository is named `vigilia` and the ROS-generated documents called the project
"Vigilia".

**Applied.** Every prose reference in the repository now reads "Vigila", along
with `ros.json`'s `project` field and the document ids in
`PROJECT-CHARTER.md` and `docs/PILOT-MEASUREMENT-PLAN.md`. The requirements
already used the source documents' spelling and were unaffected, which confirms
the values that matter downstream: `"application": "vigila"` in the manifest
([VIG-PER-013](PERSISTENCE.md#vig-per-013)), the `vigila:` commit prefix
([VIG-PER-033](PERSISTENCE.md#vig-per-033)), the default storage path `/vigila`
([VIG-PER-004](PERSISTENCE.md#vig-per-004)), and the `VIG-` identifier prefix.

**Repository renamed.** The three repository-tracking identifiers moved together
once the GitHub repository was renamed:

| Identifier | Now |
|---|---|
| GitHub repository | `kemiller2002/vigila` |
| `ros.json` → `name`, `repository.id` | `vigila` |
| `package-lock.json` → `name` | `vigila` |

**Not rewritten.** ROS work and telemetry records written before the rename
still carry `"repository": "vigilia"`. They accurately record the identity in
effect when they were written, and rewriting an audit trail to match a later
decision would make it less trustworthy, not more. New records carry `vigila`.

Nothing in this repository now refers to the product or the repository as
"Vigilia" outside those historical records.


---

<a id="oq-04"></a>

## OQ-04 — Pinned items are in the sort policy but deferred — **RESOLVED**

**Resolved 2026-09-18.** The v1 `Now` sort policy has four tiers. The pinned
tier keeps its position, reserved, and fills when pinning ships.

**Was.** §82's policy has five tiers with "pinned items" fourth, while §72 calls
pinning a future enhancement and §98 defers it — so the v1 policy referenced a
v1-absent feature.

**Why not pull pinning into v1 instead.** A sort policy mentioning a feature is
not a demonstrated requirement for it, and [VIG-GOV-018](GOVERNANCE.md#vig-gov-018)
asks features to justify their own complexity. Overriding an explicit deferral
to satisfy a placeholder is the wrong direction; the tier existing on paper is
the thing to fix.

**Applied.** [VIG-QRY-011](QUERY.md#vig-qry-011) states four tiers, names the
reserved position so a later change slots in rather than renumbering, and
documents the tier-4 secondary rule.

**Note on the division of labour.** Pinning and `Important`
([OQ-05](#oq-05)) are not the same feature and should not be merged. `Important`
decides **membership** in `Now`; pinning would decide **ordering** within it.
Keeping them apart is what lets §100's "presentation and filtering only"
and §82's ordering tiers both stand.

---

<a id="oq-05"></a>

## OQ-05 — What marks an item "for current attention"? — **RESOLVED**

**Resolved 2026-09-18.** `Important` is the marker.

**Was.** §16.1 puts "open items explicitly marked for current attention" in
`Now`, and no source document says which field marks them.

**Why `Important`.** It is the only one of the three candidates that is both in
v1 and about the user's own judgement of significance:

| Candidate | Why not |
|---|---|
| `NeedsReview` (§114) | About an *agent-created* item awaiting human check. A different question from "this matters now". |
| Pinning (§72) | Deferred by §98, and §72's own example is about keeping something *at the top* — ordering, not membership ([OQ-04](#oq-04)). |

**The §100 tension, resolved.** §100 says `Important` must affect "presentation
and filtering only" and must not change workflow state. Appearing in `Now` is
presentation: `Now` is a derived view, not a state, and
[VIG-TIME-024](TIME.md#vig-time-024) already treats view membership as
calculated rather than stored. Marking an item Important changes no status and
no transition, so §100 holds.

**Applied.** [VIG-UI-002](UI.md#vig-ui-002) names the field;
[VIG-DOM-038](DOMAIN.md#vig-dom-038) records that `Now` membership is one of the
presentation effects it permits. No new field was added — the alternative was a
fourth boolean meaning almost exactly what `Important` already means.

---

<a id="oq-06"></a>

## OQ-06 — Where do snoozed items appear? — **RESOLVED**

**Resolved 2026-09-18.** `Upcoming` widens to include snooze expiry.

**Was.** v0.3 §56 requires snoozed items to leave `Now` until the snooze
expires, without saying which view holds them meanwhile. They are not
`Deferred` (§56 is explicit), and §16.3 describes `Upcoming` as future *due and
follow-up* dates — so a snoozed item with neither date would appear in no
primary view at all, colliding with
[VIG-QRY-013](QUERY.md#vig-qry-013).

**Decided.** [VIG-UI-004](UI.md#vig-ui-004) covers future due dates, future
follow-up dates, **and** snooze expiry.

**Why here rather than elsewhere.**

- A snooze-until *is* a future moment at which an item returns to attention,
  which is what the other two dates in `Upcoming` already are. It is the same
  kind of fact, not a new one.
- It needs no new view, so
  [VIG-UI-001](UI.md#vig-ui-001)'s "deliberately small number of views" holds.
- It closes the [VIG-QRY-013](QUERY.md#vig-qry-013) hole directly: every open
  item now appears in at least one primary view.

**Rejected alternatives.**

- **Inbox.** [VIG-UI-006a](UI.md#vig-ui-006a) is for items missing
  *organisation* — no tags, no dates, no next action. A snoozed item is the
  opposite: someone made a deliberate decision about when to see it again.
  Putting it there would ask the user to re-process work they already
  processed.
- **A dedicated Snoozed view.** Costs a primary view against
  [VIG-UI-001](UI.md#vig-ui-001) to hold items that are, by definition, ones
  the user asked not to think about yet.
- **Nowhere, reachable only by search.** What the requirements accidentally
  specify, and what [VIG-QRY-013](QUERY.md#vig-qry-013) forbids.

**Consequence.** `Upcoming` needs to show *why* a row is there — due, follow-up
or snooze — since the same item may qualify on more than one, and
[VIG-UI-020](UI.md#vig-ui-020) forbids carrying that distinction by colour
alone.


---

<a id="oq-07"></a>

## OQ-07 — Is the Inbox view in v1? — **RESOLVED**

**Resolved 2026-09-18.** Yes, at SHOULD level, with fixed documented criteria.

**Was.** §113 describes Inbox in requirement language but it appears in no scope
list: not in §54, not elevated by §97 or §141, not deferred by §98 or §142 —
which defers only *advanced Inbox customisation*, implying the basic view is not
deferred.

**Why v1.** Inbox is not new scope; it is the mechanism for a requirement
already in v1. [VIG-QRY-013](QUERY.md#vig-qry-013) requires that open items with
no dates stay discoverable and that "a review mechanism SHOULD periodically
surface undated items". Inbox *is* that mechanism. Building it costs a derived
query rather than a new concept — §113 is explicit that Inbox must not be a
workflow state.

**Criteria, fixed for v1.** An open item is in `Inbox` when either:

- it carries no organising signal at all — no tags, no due date, no follow-up
  date and no next action; or
- `NeedsReview` is set.

The conjunction is deliberate. Taking §113's list disjunctively would hold every
item in `Inbox` until it was fully annotated, which turns a processing queue
into a nag. The conjunction captures the genuinely unprocessed, and adding any
one signal removes the item automatically, as §113 requires.

§113 asks for criteria "configurable or documented". Documented is the cheaper
limb and the one §142 leaves in v1; configurability rides with the deferred
customisation work.

---

<a id="oq-08"></a>

## OQ-08 — Storage path and organisation layout do not compose cleanly — **RESOLVED**

**Resolved 2026-09-18** by [ADR-0002](../architecture/ADR-0002-storage-layout.md).

**Was.** v0.2 §27 gives `/vigila/organizations/<org>/items/`. v0.3 §61 makes
the storage path configurable with default `/vigila` and shows sibling
applications at the repository root. v0.4 §124 adds `WorkspaceId` without
saying where it sits. The three were never stated to compose.

**Decided.**

```
<storagePath>/
  manifest.json                    application identity + schema version
  workspaces/
    <workspaceId>/
      workspace.json               display name, created-at
      items/
        <itemId>.json              one file per item
```

- `organizations/` becomes `workspaces/`, keyed on the stable `WorkspaceId`
  rather than a display name, because [VIG-PER-007](PERSISTENCE.md#vig-per-007)
  requires identity to survive a rename.
- The application manifest sits at the storage root; per-workspace metadata
  sits inside the workspace.
- One file per item, flat, named by the immutable id. The file's blob SHA
  serves as the concurrency token
  ([VIG-AGT-032](AGENT.md#vig-agt-032)).

**The conflict it forced.** [VIG-PER-044](PERSISTENCE.md#vig-per-044) (SHOULD)
says a list view should not cost one request per item;
[VIG-UI-026](UI.md#vig-ui-026) (MUST) forbids indexes until scale requires
them. One file per item cannot satisfy both. Resolved in favour of UI-026 as
the stronger level: v1 has no index and accepts N reads. The layout is shaped
so an index can be added later without moving files, and
[VIG-QRY-021](QUERY.md#vig-qry-021)'s compact result already describes its row
shape.

Implemented in `src/Vigila.Host.GitHub/StorageLayout.fs`, with path-traversal
coverage per [VIG-TST-016](QUALITY.md#vig-tst-016).


---

<a id="oq-09"></a>

## OQ-09 — `Cancelled` is both a state and a resolution classification — **RESOLVED**

**Resolved 2026-09-18.** `Cancelled` and `CompletedSuccessfully` are removed
from the resolution enumeration. Four classifications remain: `Superseded`,
`PromotedToRos`, `NoLongerRelevant`, `Other`.

**Was.** §3 makes `Cancelled` a workflow state; §105 also lists it as a
resolution classification, alongside `CompletedSuccessfully`. An item in state
`Cancelled` with classification `Cancelled` states one fact twice, and nothing
says which encoding wins.

**Why remove rather than keep both.** §105 is explicit that classification "must
not replace state history" — so where state already carries the fact, the
classification adds nothing but a second place to disagree. The value of the
classification is in what state *cannot* express: that an item was superseded,
promoted to ROS, or simply stopped mattering. Keeping the redundant two would
leave [VIG-AGT-022](AGENT.md#vig-agt-022)'s "deterministically implied" with no
determinate answer, because the state already implies them.

**This is a deviation from §105's literal list,** recorded rather than silent.
It is also the reversible direction: §105 permits adding classifications later
without changing item identity, so restoring the two is additive, while removing
them after items carry them is a migration
([VIG-PER-023](PERSISTENCE.md#vig-per-023)). The same asymmetry decided
[OQ-01](#oq-01).

**Applied.** [VIG-DOM-040](DOMAIN.md#vig-dom-040) lists four classifications,
and `Resolution` in `src/Vigila.Semantic/Item.fs` matches.

---

<a id="oq-10"></a>

## OQ-10 — Clock abstraction covers deferred features — **RESOLVED**

**Resolved 2026-09-18.** No action needed; closed as recorded.

§122 lists recurrence among the things the clock abstraction must cover, and
recurrence is deferred by §98. That is not a conflict: the clock is an
abstraction over *reading the time*, and it serves the v1 cases — today,
overdue, snooze expiry, staleness, waiting age — without knowing what will
later ask it for the time.

`Clock` in `src/Vigila.Semantic/Time.fs` is a function from unit to `Instant`.
Nothing about it would need to change to support recurrence, so there is no
decision to make and nothing to build ahead of need
([VIG-GOV-018](GOVERNANCE.md#vig-gov-018)).

Recorded so that §122's mention of recurrence is not later read as evidence that
recurrence was in v1 scope.

---

<a id="oq-11"></a>

## OQ-11 — "Strong title match" is undefined — **RESOLVED**

**Resolved 2026-09-18.** A strong title match is one where **every term in the
query appears in the title as a whole word**, ignoring case.

**Was.** §116 ranks "strong title match" third without defining it, while the
same section requires ranking behaviour to be documented and to "avoid
unpredictable ranking that changes without data changes". The requirement could
not be satisfied as written.

**The definition.** Given a query split on whitespace, a title matches strongly
when each term occurs in it as a whole word, case-insensitively. So
`generator accountant` strongly matches *"Call accountant about generator"*;
`generator invoice` does not.

**Why this one.** It is deterministic, cheap, and explicable to a user in one
sentence — "all of your words are in the title". The alternatives all fail
§116's own constraint:

| Alternative | Why not |
|---|---|
| Edit distance under a threshold | The threshold is arbitrary, and results shift for reasons the user cannot see. |
| Relevance scoring (TF-IDF, BM25) | Needs corpus statistics, so a result can change when an *unrelated* item is added — precisely "ranking that changes without data changes" from the searcher's point of view. |
| Substring anywhere | Matches inside words, so `cat` hits *"communication"*. Noisy and surprising. |

Whole-word matching also avoids an index ([VIG-UI-026](UI.md#vig-ui-026)) and
needs no scoring model, which keeps ranking reproducible from the item data
alone.

**Applied.** [VIG-QRY-003](QUERY.md#vig-qry-003) carries the definition, so the
"ranking behaviour MUST be documented" clause is now discharged.
