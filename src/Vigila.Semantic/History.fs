/// Tier 1 - item history.
///
/// Application-level history, not Git history: VIG-DOM-034 forbids Git being
/// the only semantic record. It holds domain events a person would recognise,
/// never transport detail -- VIG-DOM-034a keeps retries and status codes in
/// diagnostic logs instead.
///
/// Each record names the actor and, when known, the execution that acted
/// (VIG-DOM-052), so the agent that handled an item, the human that validated
/// it and the system that generated it stay separate records (VIG-DOM-053).
///
/// Requirements: VIG-DOM-032, VIG-DOM-033, VIG-DOM-034, VIG-DOM-034a,
/// VIG-DOM-052, VIG-DOM-053.
module Vigila.Semantic.History

open Vigila.Semantic.Time
open Vigila.Semantic.Actors
open Vigila.Semantic.Provenance
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
    /// A provenance contribution (VIG-DOM-055): what was done in the
    /// interchange vocabulary, why, and on what evidence.
    | Contributed of operations: ContributionOperation list * reason: string option * evidence: string list

/// One history record (VIG-DOM-033, VIG-DOM-052).
[<NoComparison>]
type HistoryEntry =
    { At: Instant
      Actor: Actor
      /// The run that performed the operation. `None` when it is not known --
      /// every record written before executions were recorded, and a UI action,
      /// which runs in no execution. Never inferred.
      Execution: ContributionKey option
      Operation: HistoryOperation }

[<RequireQualifiedAccess>]
module History =

    /// A record with no known execution.
    let entry at actor operation =
        { At = at
          Actor = actor
          Execution = None
          Operation = operation }

    /// A record attributed to an actor and, when known, its execution.
    let attributed at (attribution: Attribution) operation =
        { At = at
          Actor = attribution.Actor
          Execution = attribution.Execution
          Operation = operation }

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
