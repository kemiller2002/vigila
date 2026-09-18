---
id: REQ-TST
title: Testing and documentation obligations
status: draft
sources: v0.2 §49, §50, v0.3 §95, §96
---

# Testing and documentation

## Domain testing

#### VIG-TST-001 — Domain behaviour heavily unit tested
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §49, v0.1 §49

Domain behaviour MUST be heavily unit tested, covering at minimum:

| Area | Covers |
|---|---|
| State transitions | all legal transitions ([VIG-DOM-009](DOMAIN.md#vig-dom-009)) |
| Illegal transitions | rejection by the domain ([VIG-DOM-012](DOMAIN.md#vig-dom-012)) |
| Validation | required-field validation ([VIG-DOM-047](DOMAIN.md#vig-dom-047)) |
| Dates | due, follow-up, snooze semantics |
| Notes | note addition ([VIG-DOM-017](DOMAIN.md#vig-dom-017)) |
| Tags | tag operations and normalisation ([VIG-DOM-029](DOMAIN.md#vig-dom-029)) |
| Lifecycle | completion and reopening ([VIG-DOM-041](DOMAIN.md#vig-dom-041)) |
| Serialisation | round-trip fidelity |
| Schema | compatibility across versions ([VIG-PER-021](PERSISTENCE.md#vig-per-021)) |
| Concurrency | version checks ([VIG-AGT-030](AGENT.md#vig-agt-030)) |

## Integration testing

#### VIG-TST-002 — GitHub persistence tested separately
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §49, v0.1 §49

Integration tests MUST cover GitHub persistence separately from the domain.

#### VIG-TST-003 — Connection and configuration tests
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §49

Integration tests MUST cover: connection validation, invalid token behaviour,
insufficient token permission behaviour, missing repository behaviour, branch
handling, `localStorage` configuration handling, and persistence failures.

#### VIG-TST-004 — Limen testing standards
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §49, v0.1 §49

Limen interaction MUST be tested according to Limen's established testing
standards.

## Extended testing

#### VIG-TST-010 — Extended coverage
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §95, §97

The following MUST be explicitly covered in addition to the above:

- idempotent retries ([VIG-AGT-012](AGENT.md#vig-agt-012))
- optimistic concurrency conflicts ([VIG-AGT-030](AGENT.md#vig-agt-030))
- unknown-field tolerance ([VIG-PER-022](PERSISTENCE.md#vig-per-022))
- malformed record isolation ([VIG-PER-030](PERSISTENCE.md#vig-per-030))
- schema migration success and failure ([VIG-PER-023](PERSISTENCE.md#vig-per-023))
- snooze behaviour ([VIG-TIME-010](TIME.md#vig-time-010))
- undated-item review behaviour ([VIG-QRY-013](QUERY.md#vig-qry-013))
- repository initialisation idempotency ([VIG-PER-012](PERSISTENCE.md#vig-per-012))
- protected branch / write denial behaviour ([VIG-SEC-014](SECURITY.md#vig-sec-014))
- GitHub rate-limit behaviour ([VIG-PER-046](PERSISTENCE.md#vig-per-046))
- retry after network failure
- duplicate agent operation IDs ([VIG-AGT-013](AGENT.md#vig-agt-013))

#### VIG-TST-014 — Date-only values across time zones
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §95, §57

Date-only values MUST be tested across time zones, specifically that a date-only
value does not shift to the prior or following calendar date
([VIG-TIME-016](TIME.md#vig-time-016)).

#### VIG-TST-015 — Token never leaks
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §95, §86

Tests MUST verify the token never appears in logs, errors, or URLs
([VIG-SEC-005](SECURITY.md#vig-sec-005)).

#### VIG-TST-016 — Hostile input and path traversal
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §95, §86, §61

Tests MUST cover safe rendering of malicious-looking user text
([VIG-SEC-013](SECURITY.md#vig-sec-013)) and storage path validation / path
traversal prevention ([VIG-PER-006](PERSISTENCE.md#vig-per-006)).

#### VIG-TST-017 — Property-based testing
**Level:** SHOULD · **Release:** v1 · **Source:** v0.3 §95

Property-based testing SHOULD be considered for state transitions,
serialisation round-trips, migration invariants, tag normalisation, and
identifier generation.

## Documentation

#### VIG-TST-020 — Documentation serves humans and agents
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §50, v0.1 §50

Documentation MUST serve both humans and agents, explaining: what Vigila is,
what Vigila is not, domain terminology, states, legal transitions, agent
operations, persistence contracts, the integration assembly, GitHub connection
configuration, `localStorage` behaviour, token handling rules, examples, and
failure behaviour.

#### VIG-TST-021 — Documentation extensions
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §96, §97

Documentation MUST additionally include: Git-backed persistence tradeoffs, Git
history and privacy implications, the token-in-`localStorage` security model,
recovery from connection failure, repository initialisation, schema and version
compatibility, concurrency conflict behaviour, agent idempotency expectations,
operation receipts, safe handling of dates and time zones, v1 non-goals, and the
future shared Echelon connection-profile upgrade.

#### VIG-TST-022 — Worked examples in both modes
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §50, v0.1 §50

Examples SHOULD include both F# usage and conversational-agent scenarios where
appropriate.

#### VIG-TST-023 — Documented behaviours
**Level:** MUST · **Release:** v1 · **Source:** various

Several requirements elsewhere impose a documentation obligation. They are
collected here so none is missed:

| Must be documented | Requirement |
|---|---|
| Search ranking behaviour | [VIG-QRY-003](QUERY.md#vig-qry-003) |
| Page size limits | [VIG-QRY-015](QUERY.md#vig-qry-015) |
| Inbox membership criteria | [VIG-UI-006a](UI.md#vig-ui-006a) |
| Input size limits | [VIG-DOM-047](DOMAIN.md#vig-dom-047) |
| Command side effects | [VIG-AGT-021](AGENT.md#vig-agt-021) |
| `Actor` and `CreatedVia` semantics | [VIG-AGT-035](AGENT.md#vig-agt-035) |
| Sort tie-breaking rules | [VIG-QRY-011](QUERY.md#vig-qry-011) |
| Hard-deletion semantics | [VIG-PER-032](PERSISTENCE.md#vig-per-032) |
| Security-sensitive browser behaviour | [VIG-SEC-013](SECURITY.md#vig-sec-013) |
| Destructive migrations | [VIG-PER-023](PERSISTENCE.md#vig-per-023) |
