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

## OQ-01 — Does the `Reminder` kind survive?

**Conflict.** v0.1 §2 and v0.2 §2.4 require a `Reminder` item kind, and v0.2 §54
lists Reminder in v1 scope. v0.3 §85 withdraws it: "a reminder is often behavior
attached to an item rather than a separate domain kind", recommending core kinds
of `Task`, `FollowUp`, `Waiting` only.

v0.4 does not revisit it, so precedence gives v0.3 §85. But §85 is phrased as a
*recommendation* ("Recommendation:", "should only remain if real workflows
demonstrate a distinct semantic need"), and it adds a condition: "This
simplification should occur before implementation if it does not conflict with
existing accepted domain work."

**Assumed.** Reminder is withdrawn. [VIG-DOM-005](DOMAIN.md#vig-dom-005) lists
three kinds; [VIG-DOM-006](DOMAIN.md#vig-dom-006) covers reminder behaviour via
follow-up and snooze timing.

**Decision changes.** Whether the persisted kind enumeration has three members
or four, and whether v0.2 §54's scope list is authoritative where v0.3 contradicts
it. Cheap now, a schema migration later.

---

<a id="oq-02"></a>

## OQ-02 — `Waiting` is both a kind and a state

**Ambiguity.** v0.2 §2.3 defines `Waiting` as an item *kind*. v0.2 §3 defines
`Waiting` as an item *state*, with transitions into and out of it. The documents
never say how the two relate.

Three readings are possible:

1. They are independent: a `Task` can be in state `Waiting`, and kind `Waiting`
   is just a capture convenience.
2. Kind `Waiting` implies state `Waiting`, making one of them redundant.
3. Kind `Waiting` is what §85 says about `Reminder` — behaviour mistaken for a
   kind — and should also be withdrawn.

Reading 1 is the only one consistent with the rest of the documents:
[VIG-DOM-025](DOMAIN.md#vig-dom-025) attaches `WaitingSince` to the *state*
transition, and §16.2's Waiting view is described as "everything currently
blocked", which is a state question.

**Assumed.** Reading 1. Kind and state are independent;
[VIG-DOM-005](DOMAIN.md#vig-dom-005) and
[VIG-DOM-008](DOMAIN.md#vig-dom-008) are orthogonal.

**Decision changes.** Whether `SetKind(Waiting)` implies a state transition —
which would violate [VIG-AGT-020](AGENT.md#vig-agt-020) (commands do only what
they say) under reading 2.

---

<a id="oq-03"></a>

## OQ-03 — "Vigila" or "Vigilia"?

**Inconsistency.** All four source documents spell the product **Vigila**. This
repository is named **vigilia**, and `PROJECT-CHARTER.md` calls the project
"Vigilia".

**Assumed.** The source documents' spelling, "Vigila", is used throughout the
requirements, since they are the artifact being derived from.

**Decision changes.** The product name in every user-facing string, the
`"application": "vigila"` value in the manifest
([VIG-PER-013](PERSISTENCE.md#vig-per-013)), the `vigila:` commit prefix
([VIG-PER-033](PERSISTENCE.md#vig-per-033)), the default storage path `/vigila`
([VIG-PER-004](PERSISTENCE.md#vig-per-004)), and the `VIG-` identifier prefix.
The manifest value and storage path are persisted, so this is worth settling
before any data exists.

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

## OQ-06 — Where do snoozed items appear?

**Gap.** v0.3 §56 requires snoozed items to disappear from `Now` until the
snooze expires. It does not say which view, if any, shows them meanwhile.

They are not `Deferred` (§56 is explicit that snooze is not that state), so the
Deferred view is wrong. `Upcoming` shows "future due dates and follow-up dates"
(§16.3), which a snooze is neither.

A snoozed item with no due or follow-up date would therefore appear in no
primary view at all — which collides with
[VIG-QRY-013](QUERY.md#vig-qry-013)'s requirement that open items never
disappear indefinitely.

**Assumed.** Snoozed items remain reachable through search and filter, and the
gap in view coverage is real but unresolved.

**Decision changes.** Whether `Upcoming` is widened to include snooze
expiry, whether a snoozed item is surfaced in the Inbox
([VIG-UI-006a](UI.md#vig-ui-006a)), or whether a distinct indicator is needed.

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

## OQ-08 — Storage path and organisation layout do not compose cleanly

**Ambiguity.** v0.2 §27 gives the layout as
`/vigila/organizations/<org>/items/`. v0.3 §61 then makes the storage path
configurable with default `/vigila`, and shows sibling applications at the
repository root (`repo/vigila/`, `repo/chrona/`).

Whether `organizations/<org>/` sits inside the configurable path, or whether the
configurable path replaces the whole `/vigila/organizations/<org>/` prefix, is
not stated. v0.4 §124 adds `WorkspaceId` as a third organising concept without
saying where it lives in the path.

**Assumed.** The configurable path is the root of Vigila's storage and the
organisation/workspace structure sits beneath it.
[VIG-PER-003](PERSISTENCE.md#vig-per-003) and
[VIG-PER-004](PERSISTENCE.md#vig-per-004) are stated separately without
asserting a composition.

**Decision changes.** The on-disk layout, which is persisted and therefore
expensive to change once data exists.

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
