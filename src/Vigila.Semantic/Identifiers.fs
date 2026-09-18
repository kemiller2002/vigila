/// Tier 1 - Semantic Model. Stable identity.
///
/// Requirements: VIG-DOM-002, VIG-PER-040, VIG-PER-041, VIG-PER-042.
module Vigila.Semantic.Identifiers

open System

/// A Vigila item's identity. Immutable once created (VIG-PER-040) and never
/// derived from list position, file order or title text (VIG-PER-041), so a
/// title edit cannot change identity (VIG-DOM-015).
[<Struct; CustomEquality; NoComparison>]
type ItemId =
    private
    | ItemId of Guid

    member this.Value = let (ItemId v) = this in v

    /// Display form, e.g. "VIG-3f2a1c4e". The full value remains canonical;
    /// this is the human-friendly form VIG-PER-042 permits alongside it.
    member this.Display =
        let (ItemId v) = this
        "VIG-" + v.ToString("N").Substring(0, 8)

    /// The form used as a filename. Lowercase hex with no separators, so it is
    /// deterministic and safe across repository environments (VIG-PER-043),
    /// and derived only from the immutable id (VIG-PER-041).
    member this.Segment =
        let (ItemId v) = this
        v.ToString("N")

    override this.Equals other =
        match other with
        | :? ItemId as o -> o.Value = this.Value
        | _ -> false

    override this.GetHashCode() =
        let (ItemId v) = this
        v.GetHashCode()

[<RequireQualifiedAccess>]
module ItemId =

    /// Generates a fresh identity. Uses a GUID so that concurrent creators in
    /// separate browser sessions or agents cannot collide (VIG-PER-042).
    let create () = ItemId(Guid.NewGuid())

    let ofGuid (value: Guid) = ItemId value

    let toGuid (id: ItemId) = id.Value

    let parse (text: string) =
        match Guid.TryParse text with
        | true, value -> Ok(ItemId value)
        | _ -> Error $"'%s{text}' is not a valid item identifier."

/// A note's identity. Separate type from ItemId so the two cannot be passed
/// interchangeably (VIG-GOV-012).
[<Struct; CustomEquality; NoComparison>]
type NoteId =
    private
    | NoteId of Guid

    member this.Value = let (NoteId v) = this in v

    override this.Equals other =
        match other with
        | :? NoteId as o -> o.Value = this.Value
        | _ -> false

    override this.GetHashCode() =
        let (NoteId v) = this
        v.GetHashCode()

[<RequireQualifiedAccess>]
module NoteId =

    let create () = NoteId(Guid.NewGuid())

    let ofGuid (value: Guid) = NoteId value

    let toGuid (id: NoteId) = id.Value

/// A workspace's identity.
///
/// A workspace is the scope a set of items belongs to - what v0.2 section 27
/// called an "organization" before v0.4 section 124 named it and gave it stable
/// identity.
///
/// Deliberately opaque and separate from any display name. VIG-PER-007 requires
/// identity to survive the repository moving, and a name cannot: renaming an
/// organisation would otherwise rewrite every path that contains it. The
/// human-readable name lives beside the id, not in it.
///
/// Requirements: VIG-PER-007, VIG-PER-040.
[<Struct; CustomEquality; NoComparison>]
type WorkspaceId =
    private
    | WorkspaceId of Guid

    member this.Value = let (WorkspaceId v) = this in v

    /// The form used as a path segment. Lowercase hex with no separators, so it
    /// is deterministic and safe on every supported repository environment
    /// (VIG-PER-043).
    member this.Segment =
        let (WorkspaceId v) = this
        v.ToString("N")

    override this.Equals other =
        match other with
        | :? WorkspaceId as o -> o.Value = this.Value
        | _ -> false

    override this.GetHashCode() =
        let (WorkspaceId v) = this
        v.GetHashCode()

[<RequireQualifiedAccess>]
module WorkspaceId =

    let create () = WorkspaceId(Guid.NewGuid())

    let ofGuid (value: Guid) = WorkspaceId value

    let toGuid (id: WorkspaceId) = id.Value

    let parse (text: string) =
        match Guid.TryParse text with
        | true, value -> Ok(WorkspaceId value)
        | _ -> Error $"'%s{text}' is not a valid workspace identifier."
