/// Tier 1 - tags.
///
/// Flat, user-defined, and normalised so that "Tax", "tax " and "tax" are one
/// tag rather than three (VIG-DOM-029). Normalisation happens in the
/// constructor, so an un-normalised Tag cannot exist.
///
/// Requirements: VIG-DOM-026, VIG-DOM-027, VIG-DOM-028, VIG-DOM-029,
/// VIG-DOM-047.
module Vigila.Semantic.Tags

open System

/// A single tag.
type Tag =
    private
    | Tag of string

    member this.Value = let (Tag v) = this in v

    override this.ToString() = this.Value

[<RequireQualifiedAccess>]
module Tag =

    /// Documented practical limit (VIG-DOM-047).
    [<Literal>]
    let MaxLength = 64

    /// Normalises and validates a tag.
    ///
    /// Lowercased so that case cannot fork a tag in two, trimmed, and interior
    /// runs of whitespace collapsed to a single space. Invariant lowercasing is
    /// used deliberately: culture-sensitive casing would make tag identity
    /// depend on the reader's locale, which would break VIG-DOM-028's
    /// intersection queries across machines.
    let create (text: string | null) =
        match text with
        | Null -> Error "A tag is required."
        | NonNull supplied ->
            let collapsed =
                supplied.Split([| ' '; '\t'; '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
                |> String.concat " "

            let normalised = collapsed.ToLowerInvariant()

            if normalised.Length = 0 then
                Error "A tag is required."
            elif normalised.Length > MaxLength then
                Error $"A tag may be at most %d{MaxLength} characters; got %d{normalised.Length}."
            else
                Ok(Tag normalised)

    let value (tag: Tag) = tag.Value

/// A set of tags on one item.
///
/// A Set rather than a list: VIG-DOM-029 forbids storing the same tag twice on
/// one item, and a set makes that structural rather than a rule to enforce.
type TagSet = Set<Tag>

[<RequireQualifiedAccess>]
module TagSet =

    let empty: TagSet = Set.empty

    let add tag (tags: TagSet) = Set.add tag tags

    let remove tag (tags: TagSet) = Set.remove tag tags

    let contains tag (tags: TagSet) = Set.contains tag tags

    /// Parses many tags at once, reporting every failure rather than only the
    /// first, so a caller fixing input does not have to iterate
    /// (VIG-DOM-047 requires explicit validation errors).
    let ofStrings (texts: string seq) =
        let results = texts |> Seq.map Tag.create |> Seq.toList

        let failures =
            results
            |> List.choose (function
                | Error e -> Some e
                | Ok _ -> None)

        if List.isEmpty failures then
            results
            |> List.choose (function
                | Ok t -> Some t
                | Error _ -> None)
            |> Set.ofList
            |> Ok
        else
            Error failures

    /// Whether every tag in `required` is present -- the intersection query
    /// VIG-DOM-028 asks for ("open items tagged culinary and sales").
    let containsAll (required: TagSet) (tags: TagSet) = Set.isSubset required tags

    let toSortedList (tags: TagSet) = tags |> Set.toList |> List.map Tag.value
