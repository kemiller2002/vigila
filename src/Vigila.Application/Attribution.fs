/// Tier 3 - attributed operations on an item.
///
/// When a caller says who is acting and in which execution, a change to an
/// item also appends that actor's contribution to the item's provenance block
/// (VIG-PROV-005). The domain change is Tier 1/Tier 2's; this module only pairs
/// it with the contribution, and returns an item only when both succeed, so a
/// change can never be recorded without its contribution or the other way
/// round.
///
/// Nothing here grants or refuses an operation because of who the actor is
/// (VIG-PROV-014): a refusal is always about the transition or about the
/// provenance rules, never about identity.
///
/// Requirements: VIG-PROV-003, VIG-PROV-005, VIG-PROV-008, VIG-PROV-014,
/// VIG-PROV-015, VIG-PROV-016.
module Vigila.Application.Attribution

open Vigila.Semantic.Time
open Vigila.Semantic.Items
open Vigila.Semantic.Notes
open Vigila.Semantic.Item
open Vigila.Semantic.Provenance
open Vigila.Transition

/// Who is acting, in which execution, and when. Taken from an explicit
/// declaration (an envelope, flags, `ROS_EXECUTION_ID`); never discovered here.
type Attribution =
    { Actor: ProvenanceActor
      /// `EXE-...`, `EXT-<system>.<run-id>` or `CTB-...`. `None` when only the
      /// operation id is known; the key is then `EXT-op.<operationId>`.
      Execution: string option
      OperationId: string
      At: Instant
      Reason: string option }

/// Why an attributed operation was refused.
type AttributionError =
    /// The status change is not legal (VIG-DOM-009); maps to InvalidTransition.
    | TransitionRefused of Transitions.TransitionRefusal
    /// The provenance rules refused the contribution (re-attribution, a
    /// second `created`, an unsupported block); maps to ValidationFailed.
    | ProvenanceRefused of string
    /// The input itself is invalid; maps to ValidationFailed.
    | Invalid of string

[<RequireQualifiedAccess>]
module Attribution =

    /// The contribution key this attribution files under (VIG-PROV-004).
    let key (attribution: Attribution) =
        attribution.Execution
        |> Option.defaultWith (fun () -> Provenance.operationKey attribution.OperationId)

    let private clock attribution = Clock.fixedAt attribution.At

    let private legacyActor attribution = ProvenanceActor.toLegacy attribution.Actor

    /// Appends the attribution's contribution to the item's own block. A legacy
    /// item gains a block whose first entry is this contribution: its origin
    /// stays unknown rather than being invented (VIG-PROV-008).
    let contribute operations attribution (item: Item) =
        let block =
            match item.Provenance with
            | Some(CarriedVerbatim(schema, _)) ->
                Error $"this item's provenance is %s{schema}, which this version carries verbatim and never appends to"
            | Some(Recorded block) -> Ok block
            | None -> Ok Provenance.empty

        let entry =
            { Provenance.contribution operations attribution.At attribution.Actor with
                Reason = attribution.Reason }

        block
        |> Result.bind (Provenance.append (key attribution) entry)
        |> Result.map (fun (updated, _) -> { item with Provenance = Some(Recorded updated) })
        |> Result.mapError ProvenanceRefused

    /// Changes status. Closing (Completed or Cancelled) is recorded as
    /// `resolved`; any other change as `modified`.
    let changeStatus attribution target resolution resolutionNote (item: Item) =
        let operation =
            match target with
            | Completed
            | Cancelled -> Operation.Resolved
            | _ -> Operation.Modified

        Transitions.changeStatus
            (clock attribution)
            (legacyActor attribution)
            (Some(key attribution))
            target
            resolution
            resolutionNote
            item
        |> Result.mapError TransitionRefused
        |> Result.bind (contribute [ operation ] attribution)

    /// Resolves an item: completes or cancels it, recorded as `resolved`.
    let resolve attribution target resolution resolutionNote item =
        match target with
        | Completed
        | Cancelled -> changeStatus attribution target resolution resolutionNote item
        | other -> Error(Invalid $"resolving means completing or cancelling, not moving to %A{other}")

    /// Adds a note, recorded as `modified`. The note and its history entry
    /// point at the contribution (VIG-PROV-016).
    let addNote attribution text (item: Item) =
        Note.create (clock attribution) (legacyActor attribution) item.Id text
        |> Result.mapError Invalid
        |> Result.bind (fun note ->
            let attributed = { note with Contribution = Some(key attribution) }
            Item.addNote (clock attribution) (legacyActor attribution) attributed item
            |> contribute [ Operation.Modified ] attribution)

    /// Records a review, `reviewed`, and clears the review flag if it was set.
    /// Reviewing does not modify the item's content (RQ-ROS-2026-A014).
    let review attribution (item: Item) =
        item
        |> Item.setNeedsReview (clock attribution) (legacyActor attribution) (Some(key attribution)) false
        |> contribute [ Operation.Reviewed ] attribution
