---
id: PROJECT-CHARTER-vigila
title: Vigila Project Charter
status: draft
version: 0.1.0
created: 2026-09-17
updated: 2026-09-17
---

# Vigila project charter

## Purpose

Vigila is the system of record for personal and business follow-ups, open
loops, reminders, waiting items, and lightweight tasks — the things that stay
mentally open until something closes them. It is deliberately not a
project-management system and not a replacement for ROS
([VIG-GOV-001](docs/requirements/GOVERNANCE.md) through `VIG-GOV-003`).

## Intended users

Individuals tracking their own follow-ups, and the conversational agents acting
on their behalf. Agent interaction is a primary requirement rather than an
afterthought (`VIG-AGT-001`).

Vigila is **the application, not one person's instance.** Many people will run
it against their own data repositories, each supplying their own GitHub token
and `owner/repository` (`VIG-SEC-001`). This repository holds the application;
item data lives in a separate repository per user. Nothing in the application
may assume a single tenant, a single repository, or a single organisation
(`VIG-PER-003`).

## First bounded outcome

**Basic entry.** Capture an item and see it again — the shortest path that
exercises the whole stack: fast capture (`VIG-UI-010`, `VIG-UI-011`) through
the F# domain, persisted to a configured GitHub repository
(`VIG-PER-001`), and read back into a primary view.

This is the first slice because every later requirement depends on items
existing and round-tripping. Everything else in v1 scope follows it.

## Included

- Definition of the first user and communication problem.
- A working vertical slice.
- Evidence and decision traceability.
- Evaluation of the Repository Operating System pilot.

## Excluded

- Broad discipline claims without comparative evidence.
- An exhaustive communication taxonomy.
- Autonomous acceptance of research or policy.
- Production handling of secrets or sensitive communication data before a
  privacy and threat review.

## Success criteria

- The first vertical slice has observable acceptance tests.
- Material decisions cite their evidence and alternatives.
- A successor can continue from repository records without chat history.
- Pilot measurements can compare the operating approach with a declared
  lightweight baseline.

## Constraints and assumptions

- Constraints: not yet established.
- Assumption: a bounded communication problem can be selected without first
  resolving the full disciplinary boundary.

## Owners and decision authority

Not yet assigned.
