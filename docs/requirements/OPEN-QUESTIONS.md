---
id: REQ-OQ
title: Open questions from requirements derivation
status: open
created: 2026-09-18
---

# Open questions

Points where deriving the requirements needed a judgement the source documents
do not settle. Each records what the sources say, what the requirement set
currently assumes, and what a decision would change.

Nothing here blocks reading the requirements — each has a working assumption
already applied. They are listed because the assumption may be wrong, and
because several are cheaper to settle before implementation than after.

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

## OQ-04 — Pinned items are in the sort policy but deferred

**Gap.** v0.3 §82's `Now` sort policy has five tiers, and tier 4 is "pinned
items". v0.3 §72 classifies pinning as a future enhancement, and §98 lists it in
the deferred backlog.

So the v1 sort policy references a v1-absent feature.

**Assumed.** Tier 4 is empty in v1 and the policy degrades to four tiers.
[VIG-QRY-011](QUERY.md#vig-qry-011) records this.

**Decision changes.** Either pinning moves into v1 (it is described as
low-cost), or the documented v1 sort policy should say four tiers so the
implementation is not written against a placeholder.

---

<a id="oq-05"></a>

## OQ-05 — What marks an item "for current attention"?

**Gap.** v0.2 §16.1 says the `Now` view includes "open items explicitly marked
for current attention". No source document defines the field that marks them.

Candidates that exist elsewhere: `Important` (v0.4 §100), pinning (v0.3 §72,
deferred), or `NeedsReview` (v0.4 §114). None is described as doing this job.

**Assumed.** No dedicated field. [VIG-UI-002](UI.md#vig-ui-002) restates §16.1
verbatim, leaving the marker unspecified.

**Decision changes.** Whether v1 needs a fourth boolean on the item, or whether
`Important` is meant to serve this purpose — in which case
[VIG-DOM-038](DOMAIN.md#vig-dom-038)'s "presentation and filtering only"
constraint needs re-reading, since inclusion in `Now` is arguably more than
presentation.

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

## OQ-07 — Is the Inbox view in v1?

**Classification ambiguity.** v0.4 §113 describes the Inbox view in present-tense
requirement language ("Vigila should support a derived Inbox view"), but it is
not in v0.2 §54's v1 scope list, not elevated by §97 or §141, and not deferred
by §98 or §142. Only "advanced Inbox customisation" is deferred, by §142 —
implying the basic view is not.

**Assumed.** v1, at SHOULD level. [VIG-UI-006a](UI.md#vig-ui-006a).

**Decision changes.** Whether v1 ships five primary views or six.

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

## OQ-09 — `Cancelled` is both a state and a resolution classification

**Overlap.** v0.2 §3 defines `Cancelled` as a workflow state. v0.4 §105 lists
`Cancelled` as one of the optional resolution classifications, alongside
`CompletedSuccessfully`, `Superseded`, `PromotedToRos`, `NoLongerRelevant`,
`Other`.

An item in state `Cancelled` with classification `Cancelled` carries the same
fact twice; an item in state `Cancelled` with classification `NoLongerRelevant`
carries two different facts. The relationship is not defined.

**Assumed.** Classification is independent optional metadata that does not
constrain or derive from state, per §105's "must not replace state history".
[VIG-DOM-040](DOMAIN.md#vig-dom-040).

**Decision changes.** Whether the classification enumeration should drop
`Cancelled` (and arguably `CompletedSuccessfully`) as redundant with state, and
whether any validation couples the two.

---

<a id="oq-10"></a>

## OQ-10 — Clock abstraction covers deferred features

**Minor.** v0.4 §122 requires the clock abstraction to cover "today, overdue,
snooze expiration, recurrence, staleness, and waiting age". Recurrence is
deferred by §98 ([VIG-TIME-025](TIME.md#vig-time-025)).

**Assumed.** The clock abstraction is built for the v1 cases and must not
preclude recurrence. [VIG-TIME-023](TIME.md#vig-time-023) lists the full set as
§122 states it.

**Decision changes.** Nothing structural — noted so the recurrence mention is
not read as pulling recurrence into v1.

---

<a id="oq-11"></a>

## OQ-11 — "Strong title match" is undefined

**Gap.** v0.4 §116's ranking places "strong title match" third, between exact
title match and tag match. No source defines what makes a title match "strong",
and §116 simultaneously requires that search "avoid unpredictable ranking".

**Assumed.** Undefined, and flagged rather than invented.
[VIG-QRY-003](QUERY.md#vig-qry-003) reproduces the ordering as given.

**Decision changes.** Whether v1 implements a documented, testable definition
(prefix match, token subset, edit distance under a threshold) or collapses tiers
2–3 until one is chosen. §116's "ranking behaviour should be documented" cannot
be satisfied without settling this.
