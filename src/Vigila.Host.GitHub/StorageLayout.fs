/// Tier 4 - the on-disk shape of a Vigila workspace in its repository.
///
/// Pure path computation: no I/O, no GitHub, no clock. It lives at Tier 4
/// because a repository path is storage vocabulary and the domain must not
/// learn it (VIG-PER-001).
///
/// The layout, decided in ADR-0002:
///
///     <storagePath>/
///       manifest.json                    application identity + schema version
///       workspaces/
///         <workspaceId>/
///           workspace.json               display name, created-at
///           items/
///             <itemId>.json              one file per item
///
/// Requirements: VIG-PER-003, VIG-PER-004, VIG-PER-005, VIG-PER-006,
/// VIG-PER-007, VIG-PER-013, VIG-PER-043, VIG-PER-044.
module Vigila.Host.GitHub.StorageLayout

open System
open Vigila.Semantic.Identifiers

/// Why a configured storage path was refused.
type PathRefusal =
    | Empty
    | Traversal of segment: string
    | RelativeSegment
    | BackslashSeparator
    | ControlCharacter
    | AbsoluteOrRooted of string

    member this.Describe =
        match this with
        | Empty -> "A storage path is required."
        | Traversal segment -> $"A storage path may not contain '..' (found '%s{segment}')."
        | RelativeSegment -> "A storage path may not contain '.' as a segment."
        | BackslashSeparator -> "A storage path must use '/' as its separator."
        | ControlCharacter -> "A storage path may not contain control characters."
        | AbsoluteOrRooted value -> $"'%s{value}' is not a repository-relative path."

/// A validated, repository-relative storage path.
///
/// Private constructor: a value of this type has already been normalised and
/// checked, so every path built from one is inside the configured area. That
/// is what makes VIG-PER-005 ("never write outside the storage path") a
/// property of the type rather than a rule each call site must remember.
[<Struct>]
type StoragePath =
    private
    | StoragePath of string

    member this.Value = let (StoragePath v) = this in v

    override this.ToString() = this.Value

[<RequireQualifiedAccess>]
module StoragePath =

    /// The default from VIG-PER-004. Written without a leading slash because
    /// every path here is repository-relative; the requirement's "/vigila"
    /// spelling is the same location.
    [<Literal>]
    let Default = "vigila"

    /// Normalises and validates a configured storage path (VIG-PER-006).
    ///
    /// Accepts a leading or trailing '/' and collapses repeated separators,
    /// because those are spelling rather than meaning. Refuses anything that
    /// could escape the configured area or behave differently across
    /// repository environments.
    let create (raw: string) =
        match raw with
        | null -> Error Empty
        | _ when raw.Contains '\\' -> Error BackslashSeparator
        | _ when raw |> Seq.exists Char.IsControl -> Error ControlCharacter
        | _ ->
            // A drive letter or UNC prefix is not repository-relative, and
            // stripping it would silently redirect the write. A leading "//"
            // is refused rather than collapsed for the same reason: "//host/share"
            // and "//vigila" are syntactically identical, so treating the first
            // as relative would quietly rewrite an author's intent. Interior
            // repeats are still collapsed, since they carry no such meaning.
            if raw.Contains ':' || raw.TrimStart().StartsWith("//", StringComparison.Ordinal) then
                Error(AbsoluteOrRooted raw)
            else
                let segments =
                    raw.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (fun s -> s.Trim())
                    // A segment that is only whitespace is empty once trimmed,
                    // and an all-whitespace path has no segments at all.
                    |> Array.filter (fun s -> s.Length > 0)

                let traversal = segments |> Array.tryFind (fun s -> s = "..")
                let relative = segments |> Array.exists (fun s -> s = ".")

                match traversal, relative with
                | Some segment, _ -> Error(Traversal segment)
                | _, true -> Error RelativeSegment
                | None, false ->
                    if segments.Length = 0 then
                        Error Empty
                    else
                        Ok(StoragePath(String.Join("/", segments)))

    /// The default storage path, already validated.
    let defaultPath =
        match create Default with
        | Ok path -> path
        | Error refusal ->
            // Unreachable: the default is a constant this module controls.
            failwith $"The default storage path is invalid: %s{refusal.Describe}"

    let value (path: StoragePath) = path.Value

/// The application manifest, identifying the storage area as Vigila's
/// (VIG-PER-013). One per storage path, not per workspace: the question it
/// answers - "does this area belong to Vigila?" - is asked once, before any
/// workspace is known.
let manifestPath (root: StoragePath) = $"%s{root.Value}/manifest.json"

/// The directory holding every workspace.
let workspacesPath (root: StoragePath) = $"%s{root.Value}/workspaces"

/// One workspace's directory, keyed on its stable id rather than its display
/// name, so renaming the workspace moves nothing (VIG-PER-007).
let workspacePath (root: StoragePath) (workspace: WorkspaceId) =
    $"%s{workspacesPath root}/%s{workspace.Segment}"

/// Per-workspace metadata: display name and created-at. Separate from the
/// application manifest so that adding a second workspace adds a file rather
/// than rewriting a shared one.
let workspaceMetadataPath (root: StoragePath) (workspace: WorkspaceId) =
    $"%s{workspacePath root workspace}/workspace.json"

/// The directory holding a workspace's items.
let itemsPath (root: StoragePath) (workspace: WorkspaceId) =
    $"%s{workspacePath root workspace}/items"

/// One item's file.
///
/// One file per item, flat, named by the immutable id (VIG-PER-041,
/// VIG-PER-043). This keeps a single-item change to a single-file write
/// (VIG-PER-044) and lets the file's blob SHA serve as the concurrency token
/// (VIG-AGT-032), which a shared file could not.
///
/// Flat rather than sharded by id prefix: sharding solves a directory-size
/// problem this application is unlikely to reach, and adding it later is a
/// mechanical migration of files whose names do not change.
let itemPath (root: StoragePath) (workspace: WorkspaceId) (item: ItemId) =
    $"%s{itemsPath root workspace}/%s{item.Segment}.json"

/// Whether a path lies inside the configured storage area.
///
/// The check VIG-PER-005 exists for. Callers should not need it - every path
/// this module returns already satisfies it - but a path arriving from
/// configuration, an import or a stored reference has not been through
/// `StoragePath.create`.
let isInside (root: StoragePath) (candidate: string) =
    not (isNull candidate)
    && let prefix = root.Value + "/" in
       candidate = root.Value || candidate.StartsWith(prefix, StringComparison.Ordinal)
