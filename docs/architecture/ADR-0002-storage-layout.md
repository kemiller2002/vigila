---
id: ADR-0002
title: Repository storage layout
status: accepted
created: 2026-09-18
work_item: WI-0007
resolves: OQ-08
---

# ADR-0002 — Repository storage layout

## Context

Three requirements describe where Vigila's data lives, and none says how they
compose:

- `VIG-PER-003` (from v0.2 §27) gives `/vigila/organizations/<org>/items/`.
- `VIG-PER-004` (from v0.3 §61) makes the storage path configurable, defaulting
  to `/vigila`, and shows sibling applications at the repository root.
- `VIG-PER-007` (from v0.4 §124) adds `WorkspaceId` as a stable workspace
  identity that must survive the repository moving.

This was recorded as OQ-08. It is the open question that gets expensive after
data exists, because changing it moves every file.

## Decision

```
<storagePath>/
  manifest.json                    application identity + schema version
  workspaces/
    <workspaceId>/
      workspace.json               display name, created-at
      items/
        <itemId>.json              one file per item
```

### D1 — The workspace segment is keyed on id, not name

v0.2 §27's "organization" and v0.4 §124's "workspace" are the same concept;
§124 is later, so its vocabulary governs and `organizations/` becomes
`workspaces/`.

The segment is the `WorkspaceId`, not a display name. `VIG-PER-007` requires
identity to survive the repository moving, and a name cannot: renaming an
organisation would rewrite every path containing it. The human-readable name
lives in `workspace.json` beside the id.

### D2 — Two metadata files, not one

The application manifest sits at the storage root, because the question it
answers — "does this area belong to Vigila?" (`VIG-PER-013`) — is asked before
any workspace is known. Per-workspace metadata sits inside the workspace, so
adding a second workspace adds a file rather than rewriting a shared one.

### D3 — The blob SHA is the concurrency token

`VIG-AGT-032` permits Git object identifiers as the concurrency mechanism.
With one file per item, the file's blob SHA already *is* an exact,
server-maintained version for that item. No invented counter, no drift.

This only works under D4's one-file-per-item choice, which is part of why that
choice was made.

### D4 — One file per item, no index

Two v1 requirements pull in opposite directions here:

| | |
|---|---|
| `VIG-PER-044` (SHOULD) | list views "SHOULD NOT require one GitHub request per item", and a single-item change should not rewrite unrelated items |
| `VIG-UI-026` (MUST) | "Caches, **indexes**, databases… MUST NOT be introduced until actual scale requires them" |

One file per item satisfies PER-044's second clause and enables D3, but listing
N items costs N requests — fixable only with an index, which UI-026 forbids
until scale demands it.

**Resolved in favour of UI-026, which is a MUST where PER-044 is a SHOULD.**
v1 ships one file per item and no index, accepting N reads for a list view.

The layout is shaped so an index can be added later without moving any file:
it would be a new file at the workspace root, derived from the item files and
never authoritative. `VIG-QRY-021`'s compact search result already describes
exactly the row shape such an index would hold, so that schema work is not
wasted when the time comes.

Item files are flat rather than sharded by id prefix. Sharding addresses a
directory-size problem this application is unlikely to reach, and adding it
later is a mechanical migration of files whose names do not change.

## Consequences

- **A list view costs one request per item.** At a few hundred items this is
  fine; GitHub's 5,000 requests/hour authenticated ceiling is the real bound.
  When it is reached, the fix is an index, and UI-026's "until actual scale
  requires them" is satisfied by having reached it.
- Path containment (`VIG-PER-005`) is a property of the `StoragePath` type
  rather than a rule each call site remembers: the constructor is private, so
  a value of that type has already been validated.
- A workspace rename changes one field in one file.
- Two defects were found by writing the tests first: `//server/share` was
  silently accepted as the relative path `server/share`, and an all-whitespace
  path normalised to empty rather than being refused. Both are now refused, and
  a leading `//` is rejected outright rather than collapsed, because
  `//host/share` and `//vigila` are syntactically identical.

## Alternatives considered

**One file holding all items.** One read per list view, but every write
rewrites the whole file, which contradicts PER-044's second clause, and the
shared blob SHA would make every concurrent edit a conflict — destroying D3.
Rejected.

**Index from day one.** Fast lists, but it is precisely the thing UI-026 names,
and an index that can drift from its source is a second source of truth
(`VIG-QRY-020` forbids that). Rejected for v1, anticipated for later.

**Sharding item files by id prefix.** Premature: it solves a scale problem this
application is unlikely to reach, at the cost of complexity now
(`VIG-GOV-018`). Rejected, with the migration path noted above.

**Keeping `organizations/` as the segment name.** Would leave two names for one
concept, and keying on the org name breaks `VIG-PER-007`. Rejected.
