---
id: REQ-PER
title: Persistence, repository layout, schema, and Git semantics
status: draft
sources: v0.2 §26–§29, v0.3 §59–§61, §69, §70, §79–§81, §90–§92, v0.4 §124, §125, §132, §133
---

# Persistence

## Storage abstraction

#### VIG-PER-001 — GitHub-backed, behind an abstraction
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §26, v0.1 §26

Initial persistence uses a GitHub repository. The persistence implementation
MUST sit behind an abstraction, and the domain MUST NOT know it is stored in
GitHub.

> v0.1 §26 said persistence "may use" GitHub-backed storage; v0.2 §26 commits to
> it. The abstraction requirement is identical in both.

#### VIG-PER-002 — Replaceable backend
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §26, v0.1 §26

The persistence model MUST permit replacement with another backend later.

## Repository layout

#### VIG-PER-003 — Segregated by organisation/workspace
**Level:** SHOULD · **Release:** v1 · **Source:** v0.2 §27, v0.1 §27

Vigila data SHOULD be segregated by organisation/workspace where applicable, and
MUST NOT assume a single Echelon Foundry repository or single application beyond
the configured target repository. The conceptual structure from §27:

```
/vigila/
    /organizations/
        /echelon-foundry/
            /items/
```

The final format SHOULD conform to the broader integration/storage patterns used
across the Echelon application family.

#### VIG-PER-004 — Configurable storage path
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §61, §97

Vigila MUST NOT assume it owns the repository root. A storage path MUST be
configurable, with a reasonable default such as `/vigila`. The default path MAY
be hidden under advanced configuration in the v1 UI.

A shared repository may therefore host several applications:

```
repo/
  vigila/
  chrona/
  summa/
  other-data/
```

#### VIG-PER-005 — Never write outside the storage path
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §61

Vigila MUST never write outside its configured storage path except where
explicitly required by a documented integration contract.

#### VIG-PER-006 — Path normalisation
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §61, §95

Storage path normalisation MUST prevent path traversal and malformed-path
behaviour. This MUST be explicitly tested
([VIG-TST-016](QUALITY.md#vig-tst-016)).

## Workspace identity

#### VIG-PER-007 — WorkspaceId
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §124

Vigila MUST define a `WorkspaceId` or equivalent stable workspace identity,
which MAY include a human-readable name. Workspace identity MUST remain stable
if the repository location changes. A repository MAY eventually host multiple
Vigila workspaces if explicitly configured; v1 MAY support one configured
workspace while still defining the concept.

#### VIG-PER-008 — Repository relocation
**Level:** MUST · **Release:** deferred · **Source:** v0.4 §125

Changing owner, repository, or path affects future connection configuration only
unless an explicit migration is performed. Migration/copy between repositories
is a separate operation; the old repository MUST remain unchanged unless the
user explicitly requests mutation, and migration MUST validate target
compatibility before copying. Migration is not required in v1.

## Initialisation

#### VIG-PER-010 — Detect and perform initialisation
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §59, §97

Vigila MUST detect whether its configured storage area has been initialised. If
not, it SHOULD offer or perform a clearly defined initialisation operation.

#### VIG-PER-011 — Minimal, non-destructive initialisation
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §59

Initialisation MUST create only the minimum required Vigila files and
directories, MUST NOT overwrite unrelated repository content, and MUST create or
record the Vigila schema/data version.

#### VIG-PER-012 — Verified, idempotent initialisation
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §59

Initialisation MUST verify that required files were successfully persisted
before reporting success. Re-running initialisation against an already
initialised repository MUST be safe, and initialisation MUST be idempotent where
practical. Partial initialisation failure MUST be detectable and recoverable.

## Application manifest

#### VIG-PER-013 — Manifest
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §60, §97

Vigila MUST maintain a small application manifest in its storage area:

```json
{
  "application": "vigila",
  "schemaVersion": 1,
  "dataVersion": 1,
  "createdAt": "..."
}
```

Vigila MUST be able to identify whether a configured storage area belongs to it.
The manifest MUST be versioned and SHOULD distinguish application identity from
data/schema versions.

#### VIG-PER-014 — Unsupported versions fail safely
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §60

An unsupported future version MUST fail safely rather than attempting unsafe
writes.

#### VIG-PER-015 — Manifest carries no credentials
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §60

The manifest MUST NOT contain credentials. Manifest changes MUST follow normal
persistence and concurrency rules.

## Record format and schema

#### VIG-PER-020 — Versioned records
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §28, v0.1 §28

Persisted records MUST use a versioned schema, and every record MUST identify
its schema version.

```json
{ "schemaVersion": 1 }
```

#### VIG-PER-021 — Readers tolerate older records
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §28, v0.1 §28

Readers MUST NOT assume all persisted records use the latest schema. Migration
and backward compatibility MUST be considered from the beginning.

#### VIG-PER-022 — Unknown-field tolerance
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §80, §97

Adding a non-breaking field to a newer record MUST NOT automatically break older
readers. Unknown fields MUST be ignored or preserved according to the
serialisation strategy. A reader MUST still reject records when the
schema/version indicates a genuinely incompatible format, and forward
compatibility MUST NOT bypass validation of known required fields.

#### VIG-PER-023 — Migration rules
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §81, §97

Data migrations MUST be explicit and deterministic:

- Migrations MUST preserve item identity.
- Migrations MUST preserve history unless a documented transformation requires
  otherwise.
- Migrations MUST preserve source references and tags where semantically valid.
- Migration operations MUST be testable.
- Migration failure MUST leave original persisted data intact.
- Destructive migrations MUST have explicit handling and documentation.
- The system MUST NOT partially migrate a repository and then report success.
- Migration version progression MUST be monotonic and auditable.

## Malformed data

#### VIG-PER-030 — Malformed records are isolated
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §79, §97

A malformed item MUST NOT necessarily prevent all other valid items from
loading. Vigila MUST identify the affected record or file sufficiently for
diagnosis, and errors MUST avoid exposing credentials.

#### VIG-PER-031 — No silent repair
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §79, v0.4 §133

Malformed records MUST NOT be silently rewritten or "fixed" without an explicit
migration or repair operation. Invalid records MUST NOT be treated as valid
domain state.

Repair operations MUST state exactly what they intend to change, SHOULD support
dry-run/preview where practical, MUST use normal concurrency and persistence
safeguards, and MUST be auditable. **Automatic silent repair is prohibited.**

## Git semantics

#### VIG-PER-032 — Git history and privacy
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §70, §97

Because Git creates persistent history, data removal has different semantics
from ordinary database updates:

- Documentation MUST explain that notes and item data persisted to Git may
  remain in repository history after later edits.
- Secrets MUST never be intentionally stored in Vigila.
- Token values MUST be automatically excluded from persistence, logs, commits,
  history, diagnostics, and UI exports.
- Future redaction/deletion functionality MUST consider Git history, not only
  current files.
- The UI MUST NOT encourage storing passwords, API keys, authentication tokens,
  or other secrets in notes.
- Hard-deletion semantics MUST be documented honestly.

#### VIG-PER-033 — Commit conventions
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §69, §97

Mutations MUST produce understandable commits. Commit messages MUST identify the
Vigila operation without unnecessarily exposing item contents:

```
vigila: create VIG-000123
vigila: update VIG-000123
vigila: add note VIG-000123
vigila: complete VIG-000123
```

Commit messages MUST never contain credentials and SHOULD avoid sensitive note
text and other unnecessary user content. Commit conventions SHOULD be
deterministic enough to aid debugging.

#### VIG-PER-034 — Atomic logical operations
**Level:** SHOULD · **Release:** v1 · **Source:** v0.3 §69

Multiple file changes belonging to one logical domain operation SHOULD be
committed atomically where the GitHub interaction model permits it.

## Identifiers

#### VIG-PER-040 — Identifiers are immutable
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §90, §97

Item IDs and Note IDs MUST never change after creation.

#### VIG-PER-041 — Identifiers do not derive from mutable data
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §90

IDs MUST NOT depend solely on list position, file name order, or mutable title
text.

#### VIG-PER-042 — Collision-resistant concurrent creation
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §90

Concurrent creators MUST be able to generate IDs without collisions.
Human-friendly display IDs MAY coexist with globally unique internal IDs.

#### VIG-PER-043 — Deterministic, portable file naming
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §90

File naming conventions MUST be deterministic and safe across supported
repository environments.

## API efficiency and rate limits

#### VIG-PER-044 — Minimal writes per operation
**Level:** SHOULD · **Release:** v1 · **Source:** v0.3 §91, §97

One logical user operation SHOULD use the minimum reasonable number of GitHub
writes. Loading list views SHOULD NOT require one GitHub request per item where
the storage layout can avoid it, and the application SHOULD avoid rewriting
unrelated items for a single-item change.

#### VIG-PER-045 — No premature caching
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §91, v0.2 §44

Optimisation MUST NOT introduce caching complexity before it is needed.

#### VIG-PER-046 — Rate limits handled explicitly
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §91, §92, §97

GitHub API rate-limit responses MUST be recognised, handled explicitly, and
surfaced clearly ([VIG-AGT-050](AGENT.md#vig-agt-050) `RateLimited`).
Rate-limit failure MUST NOT be reported as successful persistence.

#### VIG-PER-047 — Vigila is not the only writer
**Level:** MUST · **Release:** v1 · **Source:** v0.3 §92

Vigila MUST NOT assume it is the only writer. External repository changes MUST
be detected through normal refresh/version behaviour, and a user MUST be able to
refresh/reload current repository state. Unexpected remote changes MUST trigger
normal concurrency/conflict handling rather than silent overwrite.

#### VIG-PER-048 — Polling
**Level:** MAY · **Release:** future · **Source:** v0.3 §92

Automatic aggressive polling is not required for v1. Any future polling MUST
account for GitHub rate limits.

## Validation

#### VIG-PER-050 — ValidateWorkspace
**Level:** MUST · **Release:** v1 · **Source:** v0.4 §132

Vigila MUST provide a `ValidateWorkspace` operation checking at least: manifest
presence, supported schema, malformed item files, duplicate IDs, broken internal
item references, invalid tags, incompatible versions, and unexpected file
layout.

`ValidateWorkspace` MUST NOT mutate data. Results MUST be machine-readable and
MUST identify affected records sufficiently for repair. Validation MUST never
silently repair problems.

## Offline

#### VIG-PER-060 — Offline is not required but must not be precluded
**Level:** MUST · **Release:** v1 · **Source:** v0.2 §45, v0.1 §45

Full offline synchronisation is not required initially, but the architecture
MUST avoid making future offline support impossible. Failure to reach
persistence MUST be clearly communicated, and the application MUST NOT indicate
that an item was saved if persistence failed.

> Full offline synchronisation is listed in the §98 deferred backlog.
