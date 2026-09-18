---
id: REQ-GOV
title: Governing principles and engineering standards
status: draft
sources: v0.2 §0, v0.1 §0, v0.3 §99, v0.4 §143
---

# Governance

Requirements that constrain what Vigila is, how it is built, and what may be
added to it. These govern the other requirement documents: a conflict between a
feature requirement and this document resolves in favour of this document.

## Purpose and identity

#### VIG-GOV-001 — System of record for open loops
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §0.1, v0.1 §1

Vigila MUST be the system of record for personal and business follow-ups, open
loops, reminders, waiting items, and lightweight tasks.

#### VIG-GOV-002 — Not a project-management system
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §0.1, v0.1 §1

Vigila MUST NOT be a project-management system.

#### VIG-GOV-003 — Not a ROS replacement
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §0.1, v0.1 §1

Vigila MUST NOT be a replacement for ROS. The two systems remain separate
(see [VIG-AGT-060](AGENT.md#vig-agt-060)).

#### VIG-GOV-004 — Promotion into ROS
**Level:** MAY · **Release:** future · **Source:** v0.2 §0.1, v0.1 §1

Engineering work MAY be promoted from Vigila into ROS when appropriate. The
promotion contract is [VIG-AGT-061](AGENT.md#vig-agt-061).

#### VIG-GOV-005 — ROS-generated follow-ups
**Level:** MAY · **Release:** future · **Source:** v0.2 §0.1, v0.1 §1

ROS work MAY generate human follow-up items in Vigila.

## Engineering standards

#### VIG-GOV-006 — ROS-governed development
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §0.2, v0.1 §2

Vigila MUST be developed according to ROS.

#### VIG-GOV-007 — SDE principles
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §0.2, v0.1 §2

Vigila MUST follow SDE principles.

#### VIG-GOV-008 — Limen at the browser boundary
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §0.2, v0.1 §2

Vigila MUST use Limen for the browser/application boundary.

#### VIG-GOV-009 — F# for application and domain code
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §0.2, v0.1 §2

Application and domain code SHOULD be written in F# wherever technically
practical.

#### VIG-GOV-010 — Native HTML and CSS
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §0.2, v0.1 §2

HTML and CSS SHOULD remain native browser technologies.

#### VIG-GOV-011 — Minimal JavaScript
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §0.2, v0.1 §2

JavaScript SHOULD be minimised and restricted to browser interoperability where
Limen requires it.

#### VIG-GOV-012 — Illegal states unrepresentable
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §0.2, v0.1 §2

Illegal domain states SHOULD be unrepresentable where practical.

#### VIG-GOV-013 — Explicit state transitions
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §0.2, v0.1 §2

State transitions MUST be explicit.

#### VIG-GOV-014 — Explicit external effects
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §0.2, v0.1 §2

External effects MUST be explicit.

#### VIG-GOV-015 — Domain independence
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §0.2, v0.1 §2

The domain model MUST NOT depend directly on GitHub APIs, browser APIs,
`localStorage`, or UI implementation details.

> v0.2 adds `localStorage` to the list v0.1 §2 gave; the v0.2 list governs.
> The corresponding infrastructure boundary is
> [VIG-SEC-020](SECURITY.md#vig-sec-020).

## Simplicity

#### VIG-GOV-016 — Substantially simpler than ROS
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §0.3, v0.1 §3

Vigila MUST remain substantially simpler than ROS.

#### VIG-GOV-017 — No project-management concepts without justification
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §0.3, v0.1 §3

Sprints, story points, backlogs, epics, estimates, dependency graphs, resource
planning, and similar project-management concepts MUST NOT be added unless a
demonstrated requirement later justifies them.

#### VIG-GOV-018 — Complexity must be justified
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §0.3, v0.1 §3

Features MUST justify their complexity.

#### VIG-GOV-019 — Optimise for capture, retrieval, review, completion
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §0.3

The initial system SHOULD optimise for extremely fast capture, retrieval,
review, and completion.

> v0.1 §3 named only capture and retrieval; v0.2 adds review and completion.

## Feature admission

#### VIG-GOV-020 — The ROS boundary test
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §99

Every proposed feature MUST be challenged against this rule: if the feature
exists primarily to plan, estimate, coordinate, sequence, measure, or execute
engineering or project work, it belongs in ROS rather than Vigila.

#### VIG-GOV-021 — Vigila's core jobs
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §143, v0.3 §99

A proposed feature MUST improve at least one of these core jobs to be accepted:

1. capture an open loop quickly
2. preserve context
3. record follow-up history
4. identify what is waiting
5. identify what needs attention
6. make the next action obvious
7. close the loop safely
8. allow an agent to do the same deterministically

A feature that primarily adds project planning, engineering execution, resource
coordination, or work estimation belongs in ROS instead.

> [VIG-GOV-020](#vig-gov-020) and [VIG-GOV-021](#vig-gov-021) are the same test
> stated negatively and positively. Both are retained because v0.4 §143 restates
> §99 as an admission criterion rather than only a warning.
