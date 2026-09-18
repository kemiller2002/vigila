/// Tier 2 - State Transition. What legal change may happen.
///
/// This module is the single semantic authority for which status changes are
/// legal (VIG-DOM-009). No other tier may re-decide it: if Tier 3 or a UI
/// starts making the same judgement, the Tier 1/2 boundary has failed.
///
/// It performs no effects. A transition is a pure decision; persisting the
/// result is Tier 4's job (VIG-GOV-014).
///
/// Requirements: VIG-DOM-009, VIG-DOM-010, VIG-DOM-012, VIG-GOV-013.
module Vigila.Transition.Transitions

open Vigila.Semantic.Items

/// Why a transition was refused. A closed set so callers can branch on the
/// reason without parsing text (VIG-AGT-050 maps this to `InvalidTransition`).
type TransitionRefusal =
    /// The target state is not reachable from the current state.
    | NotLegal of from: ItemStatus * to': ItemStatus
    /// The item is already in the target state.
    | AlreadyInState of ItemStatus

    member this.Describe =
        match this with
        | NotLegal(from, to') -> $"An item cannot move from %A{from} to %A{to'}."
        | AlreadyInState state -> $"The item is already %A{state}."

/// The legal transitions, exactly as VIG-DOM-009 specifies them.
///
/// Open      -> Waiting, Deferred, Completed, Cancelled
/// Waiting   -> Open, Deferred, Completed, Cancelled
/// Deferred  -> Open, Waiting, Completed, Cancelled
/// Completed -> Open
/// Cancelled -> Open
///
/// Completed and Cancelled are deliberately narrow: an item may be reopened
/// (VIG-DOM-042) but may not move directly between terminal states.
let private legalTargets status =
    match status with
    | Open -> [ Waiting; Deferred; Completed; Cancelled ]
    | Waiting -> [ Open; Deferred; Completed; Cancelled ]
    | Deferred -> [ Open; Waiting; Completed; Cancelled ]
    | Completed -> [ Open ]
    | Cancelled -> [ Open ]

/// True when `target` is reachable from `current`.
let isLegal current target =
    current <> target && legalTargets current |> List.contains target

/// All states reachable from `current`, for a caller that needs to offer
/// choices rather than validate one (VIG-AGT-040 capability discovery, and the
/// UI's quick-completion affordances).
let availableFrom current = legalTargets current

/// Decides a status change.
///
/// Returns the new status on success, or the reason it was refused. Refusing
/// here rather than in the UI is what makes an illegal transition
/// unrepresentable in persisted state (VIG-DOM-012).
let transition current target =
    if current = target then Error(AlreadyInState current)
    elif isLegal current target then Ok target
    else Error(NotLegal(current, target))
