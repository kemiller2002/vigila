/// Tier 1 - the item aggregate.
///
/// What can be true of one Vigila item. This module says nothing about how an
/// item changes state; that is Tier 2 (Vigila.Transition).
///
/// Every field beyond the mandatory few is optional, because VIG-DOM-004
/// requires a title to be enough to create an item -- conversational capture
/// cannot pause to fill a form.
///
/// Requirements: VIG-DOM-013 and the field requirements it indexes,
/// VIG-PROV-002, VIG-PROV-010.
module Vigila.Semantic.Item

open Vigila.Semantic.Identifiers
open Vigila.Semantic.Time
open Vigila.Semantic.Actors
open Vigila.Semantic.Items
open Vigila.Semantic.Tags
open Vigila.Semantic.Notes
open Vigila.Semantic.History
open Vigila.Semantic.Provenance

/// How a closed item was resolved (VIG-DOM-040). Optional metadata that never
/// replaces state history.
///
/// Four cases, not the six v0.4 section 105 lists. `Cancelled` and
/// `CompletedSuccessfully` were dropped because they duplicate the `Cancelled`
/// and `Completed` statuses exactly, and section 105 is explicit that a
/// classification must not replace state history -- so where the status already
/// carries the fact, a classification saying the same thing is only a second
/// place to disagree. What remains is what a status cannot express. See OQ-09.
type Resolution =
    | Superseded
    | PromotedToRos
    | NoLongerRelevant
    | Other

/// A reference back to where an item came from (VIG-DOM-030).
///
/// Identity and display are separate fields because VIG-DOM-031 wants a
/// reference to survive a URL change: the URL is a navigation hint, the
/// external id is the identity.
[<NoComparison>]
type SourceReference =
    { Type: string
      DisplayName: string
      ExternalId: string option
      Url: string option }

/// A Vigila item.
///
/// NoComparison because ItemId is not ordered -- two items have no natural
/// order, and any order a view wants is a query concern (VIG-QRY-010), not a
/// property of the type.
[<NoComparison>]
type Item =
    { Id: ItemId
      Title: Title
      Kind: ItemKind
      Status: ItemStatus

      Description: string option
      NextAction: string option

      /// When the underlying obligation is due (VIG-TIME-001).
      Due: WhenValue option
      /// When to bring the item back to attention (VIG-TIME-004). Distinct
      /// from Due, and VIG-TIME-002 forbids conflating them.
      FollowUp: WhenValue option
      /// Presentation timing, not a workflow state (VIG-TIME-011). Snoozing
      /// never alters Due -- VIG-TIME-012.
      SnoozedUntil: Instant option

      /// Free text in v1 (VIG-DOM-024); the model must not preclude a future
      /// list of structured entities.
      WaitingOn: string option
      /// Set when the item enters Waiting, reset on re-entry (VIG-DOM-025).
      WaitingSince: Instant option

      Tags: TagSet
      Notes: Note list
      Sources: SourceReference list

      /// Presentation and filtering only; never affects workflow state
      /// (VIG-DOM-038).
      Important: bool
      /// Metadata, not a workflow state (VIG-AGT-023).
      NeedsReview: bool

      Resolution: Resolution option
      /// Preserved permanently once set (VIG-DOM-039).
      ResolutionNote: string option

      CreatedAt: Instant
      CreatedBy: Actor
      CreatedVia: CreatedVia
      UpdatedAt: Instant
      CompletedAt: Instant option
      CancelledAt: Instant option
      /// Meaningful activity only; never moved by a read (VIG-DOM-036).
      LastActivityAt: Instant

      History: HistoryEntry list

      /// This item's own Praxis provenance: who created, handled, resolved and
      /// reviewed it, each keyed by the execution that did it. Authoritative
      /// for identity when present; `CreatedBy`, `CreatedVia` and the history
      /// actors are then its display projection (VIG-PROV-002). `None` for a
      /// legacy item, which reads as unattributed and is never backfilled
      /// (VIG-PROV-008).
      Provenance: ItemProvenance option

      /// The provenance block that arrived with the request that created this
      /// item (for example the finding it follows up), kept exactly as
      /// received. It describes the item's upstream origin -- who discovered
      /// the issue -- and is lineage context, never this item's authorship
      /// (VIG-PROV-010).
      ReceivedProvenance: ItemProvenance option }

[<RequireQualifiedAccess>]
module Item =

    /// Documented practical limits (VIG-DOM-047).
    [<Literal>]
    let MaxDescriptionLength = 20000

    [<Literal>]
    let MaxNextActionLength = 500

    [<Literal>]
    let MaxWaitingOnLength = 200

    /// Creates an item from a title alone.
    ///
    /// This is the shape VIG-DOM-004 and VIG-UI-010 require: everything except
    /// the title, the actor and the channel is optional, so capture is one
    /// field. Timestamps come from the supplied clock rather than ambient time
    /// (VIG-TIME-023), which is what lets a test assert exact values.
    let create clock author via title =
        let now = Clock.now clock

        { Id = ItemId.create ()
          Title = title
          Kind = Task
          Status = Open

          Description = None
          NextAction = None
          Due = None
          FollowUp = None
          SnoozedUntil = None
          WaitingOn = None
          WaitingSince = None

          Tags = TagSet.empty
          Notes = []
          Sources = []

          Important = false
          NeedsReview = false
          Resolution = None
          ResolutionNote = None

          CreatedAt = now
          CreatedBy = author
          CreatedVia = via
          UpdatedAt = now
          CompletedAt = None
          CancelledAt = None
          LastActivityAt = now

          History = [ History.entry now author Created ]
          Provenance = None
          ReceivedProvenance = None }

    /// Records an operation: appends history, and advances UpdatedAt and
    /// LastActivityAt together (VIG-DOM-036).
    ///
    /// Everything that changes an item goes through here, so history cannot be
    /// forgotten at one call site and remembered at another.
    ///
    /// `contribution` links the history entry to the provenance contribution
    /// the change belongs to (VIG-PROV-016); `None` for an unattributed change.
    /// Public so Tier 2 can record a status change it has decided is legal.
    let recordAttributed clock author contribution operation item =
        let now = Clock.now clock

        { item with
            UpdatedAt = now
            LastActivityAt = if History.isActivity operation then now else item.LastActivityAt
            History = item.History @ [ History.attributedEntry now author contribution operation ] }

    let private record clock author operation item =
        recordAttributed clock author None operation item

    /// The history entry takes the note's own contribution link, so a note and
    /// the history row that announces it cannot disagree about who wrote it.
    let addNote clock author (note: Note) item =
        { recordAttributed clock author note.Contribution NoteAdded item with Notes = item.Notes @ [ note ] }

    /// Sets or clears the review flag (VIG-AGT-023).
    let setNeedsReview clock author contribution needsReview item =
        if needsReview = item.NeedsReview then
            item
        else
            { recordAttributed clock author contribution (ReviewFlagChanged needsReview) item with
                NeedsReview = needsReview }

    let addTag clock author tag item =
        if TagSet.contains tag item.Tags then
            item
        else
            { record clock author (TagAdded tag) item with Tags = TagSet.add tag item.Tags }

    let removeTag clock author tag item =
        if TagSet.contains tag item.Tags then
            { record clock author (TagRemoved tag) item with Tags = TagSet.remove tag item.Tags }
        else
            item

    let setNextAction clock author next item =
        if next = item.NextAction then
            item
        else
            { record clock author (NextActionChanged(item.NextAction, next)) item with NextAction = next }

    let setImportant clock author important item =
        if important = item.Important then
            item
        else
            { record clock author (ImportanceChanged important) item with Important = important }

    /// Snoozes until a moment. Deliberately does not touch Due or Status:
    /// VIG-TIME-012 requires an overdue item to stay overdue while snoozed.
    let snoozeUntil clock author until item =
        { record clock author (Snoozed until) item with SnoozedUntil = Some until }

    let clearSnooze clock author item =
        match item.SnoozedUntil with
        | None -> item
        | Some _ -> { record clock author SnoozeCleared item with SnoozedUntil = None }

    /// Whether a snooze is still suppressing the item at `now`.
    let isSnoozed now item =
        match item.SnoozedUntil with
        | Some until -> Instant.isBefore now until
        | None -> false
