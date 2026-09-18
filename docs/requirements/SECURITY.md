---
id: REQ-SEC
title: Authentication, token handling, browser security, authorization
status: draft
sources: v0.2 §47, §48, v0.3 §70, §77, §78, §86, §87
---

# Security and authentication

v0.2 §47.11 records the v1 decision plainly: **Vigila v1 authenticates to its
configured data repository using a user-supplied GitHub token stored locally in
the browser.** Everything in this document follows from that choice, including
the browser-security requirements v0.3 §86 adds precisely because the token
lives in `localStorage`.

## Connection configuration

#### VIG-SEC-001 — Required inputs
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §47.1, §47.2

Vigila MUST allow the user to provide a GitHub personal access token, a GitHub
repository identifier in `owner/repository` form (for example
`echelon-foundry/vigila-data`), and an optional branch defaulting to `main`.

#### VIG-SEC-002 — Local configuration storage
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §47.3

Vigila MUST store the GitHub token, the configured repository, and the
configured branch in browser `localStorage`, so the user does not re-enter them
on every application load.

#### VIG-SEC-003 — Setup UI
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §47.6

The initial setup UI SHOULD remain minimal:

```
GitHub Token
[________________________________]

Repository
[owner/repository_________________]

Branch
[main____________________________]

[Connect]
```

#### VIG-SEC-004 — Reconfiguration
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §47.8

A user MUST be able to change the configured token, repository, and branch
later, and MUST be able to clear locally stored authentication/configuration
from the UI.

## Token handling

#### VIG-SEC-005 — Token containment
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §47.4, v0.3 §70, §86

The token MUST never be committed to the repository, written into Vigila data
files, included in logs, included in error messages, or sent anywhere other than
the GitHub APIs required for application operation.

It MUST additionally never appear in: commits, Git history, diagnostics, UI
exports ([VIG-PER-032](PERSISTENCE.md#vig-per-032)), rendered HTML, URLs, query
strings, fragments, telemetry, analytics ([VIG-SEC-013](#vig-sec-013)),
receipts ([VIG-AGT-015](AGENT.md#vig-agt-015)), command envelopes
([VIG-AGT-035](AGENT.md#vig-agt-035)), pagination cursors
([VIG-QRY-017](QUERY.md#vig-qry-017)), status output
([VIG-AGT-041](AGENT.md#vig-agt-041)), or human-readable export
([VIG-OPS-030](OPERATIONS.md#vig-ops-030)).

> This requirement is stated in seven separate places across the source
> documents. It is consolidated here and cross-referenced rather than repeated.

#### VIG-SEC-006 — Token storage UX
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §87, §97

The user MUST understand what happens when the token is stored:

- The setup UI MUST state that the token is stored locally in the current
  browser.
- The UI MUST provide a clear "Forget token / Disconnect" action.
- Clearing the token MUST NOT delete repository data.
- Changing repository configuration MUST NOT silently delete or migrate
  existing repository data.
- The application MUST NOT display the full token after initial entry; if shown
  for editing it MUST be masked by default.
- Copying or exporting configuration MUST exclude the token unless a future
  explicit credential-export feature is designed.

## Startup and validation

#### VIG-SEC-007 — Startup sequence
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §47.5

On application startup Vigila MUST:

1. Read the token and repository configuration from `localStorage`.
2. If either required value is missing, display the connection/setup screen.
3. If both exist, attempt to connect to the configured GitHub repository.
4. Verify that the repository can be read.
5. Verify that Vigila can perform the operations required for persistence.
6. If validation succeeds, load Vigila.
7. If validation fails, clearly show the failure and return the user to
   connection configuration.

#### VIG-SEC-008 — Validate before trusting
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §47.7

Connect MUST validate the supplied configuration before treating setup as
complete. The application MUST NOT assume a token is valid merely because it
exists in `localStorage`, and MUST validate access to the configured repository.

#### VIG-SEC-009 — Capability validation
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §77, §97

Startup validation MUST test the capabilities Vigila actually requires, at
minimum: repository reachable, target branch exists, storage path readable,
schema compatible, token has required read access, and token has required write
capability for normal Vigila persistence.

A successful repository read MUST NOT be assumed to imply successful write
access. If required write capability is absent, Vigila MUST clearly report that
it cannot operate normally.

#### VIG-SEC-010 — Avoid destructive probes
**Level:** SHOULD · **Release:** v1 · **Source:** v0.3 §77

Destructive probe writes SHOULD be avoided merely to test permissions when
capability can be determined safely by another means.

#### VIG-SEC-011 — Read-only mode is explicit or absent
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §77

Read-only mode MUST NOT be introduced accidentally. If supported later it MUST
be explicit.

## Connection failures

#### VIG-SEC-012 — Explicit connection errors
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §47.10

An invalid, expired, or revoked token, or a token with insufficient repository
privileges, MUST each result in an explicit connection error. Vigila MUST NOT
present stale data as though the repository connection succeeded.

Repository-not-found and repository-not-authorized conditions SHOULD be
distinguishable where GitHub's API response permits it, and the system MUST NOT
depend on GitHub revealing inaccessible private repositories.

> The corresponding error codes are `Unauthorized`, `Forbidden`,
> `RepositoryNotFound`, and `RepositoryUnavailable`
> ([VIG-AGT-050](AGENT.md#vig-agt-050)).

## Branch rules

#### VIG-SEC-014 — Branch must permit the write strategy
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §78, §97

The configured branch MUST permit Vigila's write strategy. Vigila MUST detect or
surface failures caused by protected-branch rules. If direct writes are required
by v1, the configured branch MUST permit those writes for the supplied
credential.

#### VIG-SEC-015 — Protected-branch failures are specific
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §78

Branch protection failure MUST NOT be reported merely as a generic save failure
when a more useful error can be provided
([VIG-AGT-050](AGENT.md#vig-agt-050) `BranchUnavailable`).

#### VIG-SEC-016 — No pull-request persistence in v1
**Level:** MAY · **Release:** deferred · **Source:** v0.3 §78, §98

Vigila v1 does not need to support pull-request-based persistence. It MAY be
considered later as a separate feature.

## Browser security

#### VIG-SEC-013 — Browser security is first-class
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §86, §97

Because v1 stores a GitHub token in browser `localStorage`, browser security MUST
be treated as a first-class requirement:

- The application MUST NOT load unnecessary third-party JavaScript, and SHOULD
  prefer zero third-party runtime scripts where practical.
- A restrictive Content Security Policy SHOULD be used.
- Inline script execution SHOULD be minimised or prohibited where practical.
- User-provided text MUST never be inserted into the DOM as executable HTML.
- Notes, titles, tags, descriptions, and imported data MUST be rendered safely.
- If rich text is introduced later, sanitisation MUST be explicit and thoroughly
  tested.
- Vigila MUST never use `eval` or equivalent dynamic code execution.
- The application MUST avoid third-party analytics or telemetry that could
  observe application state unless explicitly approved later.
- Security-sensitive browser behaviour MUST be documented.

## Authorization

#### VIG-SEC-020 — Authentication stays outside the domain
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §47.9, §48

Authentication and GitHub repository access MUST remain outside the Vigila
domain layer. Domain code MUST NOT know about GitHub tokens, `localStorage`,
HTTP authentication, or GitHub REST APIs. GitHub access MUST be implemented
through an infrastructure boundary/adaptor
([VIG-GOV-015](GOVERNANCE.md#vig-gov-015)).

#### VIG-SEC-021 — v1 authorization is the token's authorization
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §48

For v1, effective authorization is primarily determined by the permissions of
the supplied GitHub token and repository access. A complex role system is not
required for the first release.

#### VIG-SEC-022 — Future permission levels
**Level:** SHOULD · **Release:** future · **Source:** v0.2 §48, v0.1 §48

The architecture SHOULD support eventual permission levels such as Read, Create,
Update, Complete, Administer.

## Deferred

#### VIG-SEC-030 — Shared Echelon connection configuration
**Level:** MAY · **Release:** deferred · **Source:** v0.2 §53, §52, v0.3 §98

Shared connection profiles across Echelon applications are explicitly **not**
part of v1. Future investigation may consider: shared connection profiles,
multiple repositories, multiple GitHub tokens, credential references, JSON
configuration import/export, shared browser credential storage, migration of
existing Vigila `localStorage` configuration, a common implementation through
Limen or another shared package, a portable configuration file containing
repository mappings, separation of repository configuration from secret
credentials, secure handling of any optional credential bundle, and a common
experience for Chrona, Summa, Strata, Vigila, and future applications.

This future work MUST NOT block or complicate the initial v1 implementation.
