/// Tier 1 - notes.
///
/// Notes are append-only: an item's history is told by adding notes, not by
/// rewriting them (VIG-DOM-019). There is deliberately no edit function here.
///
/// Requirements: VIG-DOM-017, VIG-DOM-018, VIG-DOM-019, VIG-DOM-047.
module Vigila.Semantic.Notes

open Vigila.Semantic.Identifiers
open Vigila.Semantic.Time
open Vigila.Semantic.Actors

/// One note on one item (VIG-DOM-018).
///
/// NoComparison because NoteId is deliberately not ordered: note ordering is a
/// question about time (VIG-DOM-020), never about identity.
[<NoComparison>]
type Note =
    { Id: NoteId
      ItemId: ItemId
      Text: string
      CreatedAt: Instant
      CreatedBy: Actor }

[<RequireQualifiedAccess>]
module Note =

    /// Documented practical limit (VIG-DOM-047).
    [<Literal>]
    let MaxLength = 10000

    let create clock author itemId (text: string) =
        match text with
        | null -> Error "Note text is required."
        | _ ->
            let trimmed = text.Trim()

            if trimmed.Length = 0 then
                Error "Note text is required."
            elif trimmed.Length > MaxLength then
                Error $"A note may be at most %d{MaxLength} characters; got %d{trimmed.Length}."
            else
                Ok
                    { Id = NoteId.create ()
                      ItemId = itemId
                      Text = trimmed
                      CreatedAt = Clock.now clock
                      CreatedBy = author }

    /// Notes oldest first, which is the order the UI must display them in
    /// (VIG-DOM-020).
    let chronological (notes: Note list) = notes |> List.sortBy (fun n -> n.CreatedAt)
