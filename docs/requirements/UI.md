---
id: REQ-UI
title: User interface — views, capture, completion, accessibility
status: draft
sources: v0.2 §16–§19, §42, §43, v0.3 §93, §94, v0.4 §111–§113
---

# User interface

## Primary views

#### VIG-UI-001 — Deliberately few views
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §16, v0.1 §16

The initial UI SHOULD have a deliberately small number of views.

#### VIG-UI-002 — Now
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §16.1, v0.1 §16

A `Now` view MUST show actionable items needing attention now: overdue items,
due items, follow-ups that have reached their follow-up date, and open items
explicitly marked for current attention. Snoozed items MUST be excluded until
the snooze expires ([VIG-TIME-010](TIME.md#vig-time-010)).

#### VIG-UI-003 — Waiting
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §16.2, v0.1 §16

A `Waiting` view MUST show everything currently blocked on someone or something
else.

#### VIG-UI-004 — Upcoming
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §16.3, v0.1 §16, v0.3 §56

An `Upcoming` view MUST show future due dates, future follow-up dates, and
snooze expiry.

> Snooze expiry was added by [OQ-06](OPEN-QUESTIONS.md#oq-06). §56 requires
> snoozed items to leave `Now` without saying where they go; a snoozed item with
> no other date would otherwise appear in no primary view, contradicting
> [VIG-QRY-013](QUERY.md#vig-qry-013). A snooze-until is the same kind of fact
> as the other two — a future moment at which the item returns to attention.

#### VIG-UI-004a — Upcoming shows why a row qualifies
**Level:** MUST · **Release:** v1 · **Source:** OQ-06, v0.2 §17

Because one item may qualify for `Upcoming` on more than one date, the view MUST
make clear which date put each row there. That distinction MUST NOT be carried
by colour alone ([VIG-UI-020](#vig-ui-020)).

#### VIG-UI-005 — Someday / Deferred
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §16.4, v0.1 §16

A `Someday / Deferred` view MUST show items intentionally put aside.

#### VIG-UI-006 — Completed
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §16.5, v0.1 §16

A `Completed` view MUST show completed items, and completed items MUST remain
searchable.

#### VIG-UI-006a — Inbox
**Level:** SHOULD · **Release:** v1 · **Source:** v0.4 §113

Vigila SHOULD support a derived `Inbox` view for items needing additional
organisation. Inbox MUST NOT be a workflow state. Membership MAY be derived from
missing tags, no due or follow-up date, no next action, or `NeedsReview`. The
criteria MUST be configurable or documented, and an item MUST leave Inbox
automatically when the criteria are satisfied.

> Advanced Inbox customisation is deferred by §142.

## Item presentation

#### VIG-UI-007 — Decide without opening
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §17, v0.1 §17

A list item MUST display enough information to make a decision without opening
it. The shape from §17:

```
[ ] Call accountant about generator
    Next: Send cancelled check
    Sep 22
    tax · dad

(o) CPA response about generator
    Waiting on: CPA
    Follow up Sep 22
    tax · dad
```

#### VIG-UI-008 — Not visually dense by default
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §17, v0.1 §17

The UI MUST NOT become visually dense by default. Detailed notes and history
MUST be available when an item is opened, not in the list.

## Quick completion

#### VIG-UI-009 — Complete from the list
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §18, v0.1 §18

Users MUST be able to complete an item directly from the primary list, with
minimal interaction. Accidental completion MUST be recoverable through reopening
([VIG-DOM-042](DOMAIN.md#vig-dom-042)).

## Fast capture

#### VIG-UI-010 — Capture must be extremely fast
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §19, v0.1 §19

Creating an item MUST be extremely fast. The UI MUST support entering a title
and saving immediately; additional fields MUST be optional. The user MUST NOT
have to complete a large form before capturing an idea.

#### VIG-UI-011 — Quick-entry control
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §19, v0.1 §19

A quick-entry control MUST be available from the primary UI.

#### VIG-UI-012 — Capture and continue
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §112, §141

After a successful quick capture the user MUST be able to immediately enter
another item, with focus returning to the capture control where appropriate.
Capture forms MUST clear only after successful persistence; failed capture MUST
preserve entered text. Capture-and-continue MUST NOT accidentally duplicate the
prior item — see [VIG-AGT-012](AGENT.md#vig-agt-012) for the idempotency
mechanism.

## Unsaved work

#### VIG-UI-013 — Protect unsaved edits
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §93, §97

The UI MUST protect users from losing text they are actively entering:

- Navigation that would discard unsaved note or item edits SHOULD warn the user
  where practical.
- Failed saves MUST preserve the entered text in the UI where possible.
- The application MUST NOT clear an edit form until persistence succeeds.
- Retry behaviour SHOULD preserve the intended `OperationId` where appropriate.

#### VIG-UI-014 — Never claim a failed save succeeded
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §45, §46

The application MUST NOT indicate that an item was saved if persistence failed.
Failure to reach persistence MUST be communicated clearly.

#### VIG-UI-015 — Recoverable failure states
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §46, v0.1 §46

External failures — GitHub unavailable, authorisation expired, concurrency
conflict, invalid transition, schema mismatch, network interruption — MUST NOT
silently lose user input. The UI MUST present a recoverable state.

## Accessibility

#### VIG-UI-020 — Baseline accessibility
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §42, v0.1 §42

The UI MUST be keyboard usable. Interactive controls MUST use proper semantic
HTML. Text MUST NOT depend on colour alone to communicate state. Focus states
MUST be visible. Accessibility MUST be designed in from the beginning rather
than retrofitted.

#### VIG-UI-021 — Keyboard-first core operations
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §94, §97

Because Vigila is intended for frequent use, core operations MUST be efficient
without a mouse:

- Quick capture MUST be reachable by keyboard.
- Completing an item MUST have a keyboard-accessible control.
- Adding a note MUST be keyboard accessible.
- Focus order MUST be logical.
- Dialogs and drawers MUST manage focus correctly.
- Keyboard shortcuts, if added, MUST NOT conflict with normal text entry and
  SHOULD be discoverable.

#### VIG-UI-022 — Accessibility applies to mobile
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §94

Accessibility requirements apply equally to mobile and desktop responsive
layouts where applicable.

## Mobile

#### VIG-UI-023 — Works well on a phone
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §43, v0.1 §43

The UI MUST work well on a phone. Quick capture, completion, note entry, and
review MUST NOT require desktop interaction. Conversational capture is expected
to originate from mobile frequently.

## Performance

#### VIG-UI-024 — Effectively immediate
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §44, v0.1 §44

The interface SHOULD feel effectively immediate for normal use.

#### VIG-UI-025 — Do not load everything
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §44, v0.1 §44

The application SHOULD avoid loading unnecessary history and notes for every
item on initial display. List views MAY use summaries while details are loaded
when needed ([VIG-QRY-020](QUERY.md#vig-qry-020)).

#### VIG-UI-026 — No premature infrastructure
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §44, v0.1 §44

Caches, indexes, databases, and background services MUST NOT be introduced until
actual scale requires them.

## Deferred UI features

#### VIG-UI-030 — Global command palette
**Level:** MAY · **Release:** deferred · **Source:** v0.4 §111, §142

Vigila MAY eventually provide a command palette covering create, search, add
note, complete, snooze, open waiting, and jump-to-item. It MUST be additive
rather than required for basic UI use, and its commands MUST map to the same
explicit domain operations used elsewhere.

#### VIG-UI-031 — Manual ordering
**Level:** MAY · **Release:** deferred · **Source:** v0.4 §101, §142

Vigila MAY support optional manual ordering within a view. Manual order MUST be
presentation metadata rather than core workflow state, MUST NOT alter due dates,
follow-up dates, state, or importance, and MUST NOT break stable pagination or
agent query behaviour. Where manual ordering is absent, deterministic default
sorting applies ([VIG-QRY-010](QUERY.md#vig-qry-010)).
