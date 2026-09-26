/// Tier 1 - item history.
///
/// Application-level history, not Git history: VIG-DOM-034 forbids Git being
/// the only semantic record. It holds domain events a person would recognise,
/// never transport detail -- VIG-DOM-034a keeps retries and status codes in
/// diagnostic logs instead.
///
/// Requirements: VIG-DOM-032, VIG-DOM-033, VIG-DOM-034, VIG-DOM-034a,
/// VIG-PROV-016.
module Vigila.Semantic.History

open Vigila.Semantic.Time
open Vigila.Semantic.Actors
open Vigila.Semantic.Items
open Vigila.Semantic.Tags

/// A meaningful change to an item (VIG-DOM-032).
///
/// A closed set, and each case carries the old and new values where they are
/// meaningful (VIG-DOM-033), so history can be read without re-deriving what
/// changed by diffing neighbouring records.
[<NoComparison>]
type HistoryOperation =
    | Created
    | TitleChanged of before: string * after: string
    | DescriptionChanged
    | StatusChanged of before: ItemStatus * after: ItemStatus
    | KindChanged of before: ItemKind * after: ItemKind
    | NextActionChanged of before: string option * after: string option
    | DueDateChanged
    | FollowUpDateChanged
    | WaitingOnChanged of before: string option * after: string option
    | Snoozed of until: Instant
    | SnoozeCleared
    | TagAdded of Tag
    | TagRemoved of Tag
    | NoteAdded
    | ImportanceChanged of important: bool
    | ReviewFlagChanged of needsReview: bool

/// One history record (VIG-DOM-033).
///
/// `Contribution` names the provenance contribution (an `EXE-`, `EXT-` or
/// `CTB-` key in the item's provenance block) that this entry belongs to, when
/// the operation was attributed (VIG-PROV-016). The identity lives in that
/// block; `Actor` is only its display projection (VIG-PROV-015), and is all a
/// legacy or unattributed entry has.
[<NoComparison>]
type HistoryEntry =
    { At: Instant
      Actor: Actor
      Operation: HistoryOperation
      Contribution: string option }

[<RequireQualifiedAccess>]
module History =

    let entry at actor operation =
        { At = at
          Actor = actor
          Operation = operation
          Contribution = None }

    /// An entry that belongs to a provenance contribution (VIG-PROV-016).
    let attributedEntry at actor contribution operation =
        { entry at actor operation with Contribution = contribution }

    /// Oldest first.
    let chronological (entries: HistoryEntry list) = entries |> List.sortBy (fun e -> e.At)

    /// Whether an operation counts as activity for LastActivityAt
    /// (VIG-DOM-036).
    ///
    /// Everything currently in the set does. The function exists because the
    /// requirement names "meaningful activity" specifically, and a future
    /// operation that is bookkeeping rather than activity should be excluded
    /// here rather than at each call site.
    let isActivity (_: HistoryOperation) = true
