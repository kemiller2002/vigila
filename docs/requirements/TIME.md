---
id: REQ-TIME
title: Dates, time zones, snooze, and derived state
status: draft
sources: v0.2 §9, §10, v0.3 §56–§58, v0.4 §121–§123
---

# Time and scheduling

Vigila distinguishes three date concepts that are easy to conflate. The source
documents are emphatic that they are not interchangeable.

| Concept | Means | Domain state? |
|---|---|---|
| **Due date** | The underlying obligation should be completed by this time. | No — a field. |
| **Follow-up date** | Bring this item back to my attention at this time. | No — a field. |
| **Snooze** | Temporarily remove from immediate attention until this time. | No — presentation timing ([VIG-TIME-011](#vig-time-011)). |
| **Deferred** | The user has decided not to work on this now. | **Yes** — a workflow state. |

## Due dates

#### VIG-TIME-001 — Due date
**Level:** MAY · **Release:** v1 · **Source:** v0.2 §9, v0.1 §9

An item MAY have a due date/time meaning the underlying obligation should be
completed by that time.

#### VIG-TIME-002 — Due is not follow-up
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §9, v0.1 §9

Due dates MUST NOT be conflated with follow-up dates.

#### VIG-TIME-003 — Overdue stays visible
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §9, v0.1 §9

An overdue item MUST remain visible until resolved, and Vigila SHOULD make
overdue items visually identifiable. Visual identification MUST NOT rely on
colour alone ([VIG-UI-020](UI.md#vig-ui-020)).

## Follow-up dates

#### VIG-TIME-004 — Follow-up date
**Level:** MAY · **Release:** v1 · **Source:** v0.2 §10, v0.1 §10

An item MAY have a follow-up date/time meaning "bring this item back to my
attention at this time". It MAY differ from the due date.

> Worked example from v0.2 §10: contract due September 30, follow up with client
> September 22.

#### VIG-TIME-005 — Changing follow-up must be easy
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §10, v0.1 §10

Changing the follow-up date SHOULD be easy, including conversationally: "bring
this back Friday", "remind me next month", "push this out two weeks". The agent
resolves the phrase to a concrete value ([VIG-TIME-020](#vig-time-020)).

## Snooze versus defer

#### VIG-TIME-010 — Snooze
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §56, §97

Active items MUST be snoozeable until a date/time. Snoozed items MUST disappear
from the Now view until the snooze expires, and snooze expiration MUST make the
item eligible to reappear automatically.

#### VIG-TIME-011 — Snooze is presentation, not state
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §56

Snooze MUST be treated as review/presentation timing rather than a domain state.
`Deferred` MUST remain a domain state. The two are semantically different:
snooze means "not now, resurface at T"; deferred means "I have decided not to
work on this now".

#### VIG-TIME-012 — Snooze does not rewrite the obligation
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §56

Snoozing MUST NOT erase or alter the underlying due date, and snoozing an
overdue item MUST NOT change the fact that it is overdue.

#### VIG-TIME-013 — Snooze is auditable
**Level:** SHOULD · **Release:** v1 · **Source:** v0.3 §56

Snoozing SHOULD be auditable.

## Time and time-zone semantics

#### VIG-TIME-014 — Local time zone is known
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §57, §97

Vigila MUST know the user's local time zone when interpreting or presenting
local dates and times.

#### VIG-TIME-015 — Unambiguous stored timestamps
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §57

Stored timestamps MUST use an unambiguous representation. Created, updated, and
history timestamps SHOULD be precise timestamps.

#### VIG-TIME-016 — Date-only values stay date-only
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §57, v0.4 §123

Date-only values MUST remain date-only where appropriate. A date-only value MUST
NOT accidentally shift to the prior or following calendar date because of UTC
conversion.

> This is the single most testable consequence of §57 and is called out
> explicitly in the testing requirements ([VIG-TST-014](QUALITY.md#vig-tst-014)).

#### VIG-TIME-017 — Date-or-datetime precision per field
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §57

Due and follow-up values MUST support both date-only and date-and-time forms.
Snooze values MUST support date-and-time, and MAY support date-only where
product behaviour supports it.

#### VIG-TIME-018 — Display is separate from persistence
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §57

Display formatting MUST be separate from persisted semantics.

#### VIG-TIME-019 — Time zone supplied as context, not assumed
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §57, v0.4 §123

The user's local time zone MUST NOT be embedded as an assumption in the core
domain where it can be supplied as context. The integration contract MUST NOT
assume UTC means the user's local day. The effective user/workspace time zone
SHOULD be available to calling agents.

## Natural-language dates

#### VIG-TIME-020 — Parsing belongs outside the domain
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §58

Natural-language date parsing MUST live outside Vigila's deterministic core.
Vigila MUST NOT require an LLM to function, and MUST NOT require a
natural-language date parser to function.

#### VIG-TIME-021 — Agents resolve relative phrases
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §58, v0.4 §123

Calling agents are responsible for resolving phrases such as "tomorrow", "next
Friday", "this afternoon", "in two weeks" before calling domain operations.
Vigila SHOULD receive explicit date/time or date-only values.

> Worked example from §58: "Bring this back next Tuesday afternoon" resolves to
> `2026-09-22T15:00:00-04:00` before it reaches Vigila.

#### VIG-TIME-022 — Preserve uncertainty rather than inventing precision
**Level:** SHOULD · **Release:** v1 · **Source:** v0.3 §58

Where the user's language is ambiguous, the agent SHOULD preserve the
uncertainty rather than inventing precision.

## Clock abstraction

#### VIG-TIME-023 — Explicit clock
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §122, §141

Logic for "today", overdue, snooze expiration, recurrence, staleness, and
waiting age MUST NOT call ambient system time directly inside domain logic.
Tests MUST be able to inject a deterministic clock, and clock use MUST be
standardised across the application.

## Derived state

#### VIG-TIME-024 — Derived values are calculated, not stored
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §121, §141

Values such as `IsOverdue`, `IsDueToday`, `IsStale`, `IsFollowUpReady`, and
`DaysWaiting` MUST be calculated using explicit clock and time-zone context.
Persisting a derived value requires a demonstrated need, and derived values MUST
NOT drift from canonical data.

## Recurrence

#### VIG-TIME-025 — Recurring items
**Level:** MAY · **Release:** deferred · **Source:** v0.3 §55, §98

Vigila MAY eventually support recurring items while avoiding a full calendar
engine. If implemented:

- An item MAY optionally contain a recurrence rule; recurrence MUST NOT be
  required for normal items.
- Completing a recurring item MUST create or schedule the next occurrence.
- Historical occurrences MUST remain completed rather than being overwritten.
- Recurrence MUST support at least daily, weekly, monthly, yearly, and custom
  interval, and SHOULD eventually allow an end date or occurrence limit.
- Recurrence behaviour MUST be deterministic and testable.
- Recurrence MUST NOT require a general-purpose calendar framework unless
  demonstrated requirements justify one.

> Classified in §55 as a future upgrade unless required by an early user
> workflow, and listed in the §98 deferred backlog.

## Escalation

#### VIG-TIME-026 — Escalation date
**Level:** MAY · **Release:** deferred · **Source:** v0.4 §103, §142

Vigila MAY eventually support an optional escalation date or rule. Escalation
MUST NOT replace the normal follow-up date, SHOULD represent a more serious next
step rather than another reminder, and MUST NOT occur automatically without an
explicit configured rule.
