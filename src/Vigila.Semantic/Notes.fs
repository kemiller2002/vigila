/// Tier 1 - notes.
///
/// Notes are append-only: an item's history is told by adding notes, not by
/// rewriting them (VIG-DOM-019). There is deliberately no edit function here.
///
/// Requirements: VIG-DOM-017, VIG-DOM-018, VIG-DOM-019, VIG-DOM-047,
/// VIG-PROV-016.
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
      CreatedBy: Actor
      /// The provenance contribution this note belongs to, when it was written
      /// by an attributed operation (VIG-PROV-016). `CreatedBy` is then only
      /// the display projection of that contribution's actor.
      Contribution: string option }

[<RequireQualifiedAccess>]
module Note =

    /// Documented practical limit (VIG-DOM-047).
    [<Literal>]
    let MaxLength = 10000

    let create clock author itemId (text: string | null) =
        match text with
        | Null -> Error "Note text is required."
        | NonNull supplied ->
            let trimmed = supplied.Trim()

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
                      CreatedBy = author
                      Contribution = None }

    /// Notes oldest first, which is the order the UI must display them in
    /// (VIG-DOM-020).
    let chronological (notes: Note list) = notes |> List.sortBy (fun n -> n.CreatedAt)
