/// Tier 3 - Application / Orchestration.
///
/// Declares the ports Tier 4 implements. The domain never learns what is on
/// the other side of one (VIG-PER-001, VIG-GOV-015).
///
/// This is the lowest tier that references Aegis, because it is the lowest
/// tier that coordinates calls which can fail operationally. A Tier 2
/// transition refusal is a domain answer and is not an Aegis fault; a GitHub
/// timeout is.
module Vigila.Application.Ports

open Aegis
open Vigila.Semantic.Identifiers

/// Names the operation being attempted, for the scope Aegis attaches to any
/// fault raised inside it.
type OperationName = string

/// A store of Vigila items, as the application tier sees it.
///
/// Deliberately free of GitHub vocabulary: no token, no branch, no commit, no
/// repository. Tier 4 supplies those (VIG-SEC-020). The concurrency token is
/// declared here as an opaque value so the contract exposes a stable
/// concurrency concept without naming the mechanism behind it
/// (VIG-AGT-032).
type ConcurrencyToken = ConcurrencyToken of string

/// The outcome of a write that carried an expected version.
///
/// `Conflict` is a first-class result rather than an exception, because
/// VIG-AGT-030 requires a mismatch to return a conflict the caller can act on
/// instead of overwriting newer state.
type WriteOutcome<'T> =
    | Written of 'T * ConcurrencyToken
    | Conflict of current: ConcurrencyToken
    | Failed of Fault

/// The port Tier 4 implements.
///
/// Every member returns an outcome rather than throwing, so that "we do not
/// know what happened" stays representable (VIG-UI-014: a failed save is never
/// reported as a success).
type ItemStore =
    abstract Read: ItemId -> Result<ConcurrencyToken, Fault>
    abstract Write: ItemId * ConcurrencyToken option -> WriteOutcome<ItemId>

/// Builds the Aegis scope for an operation in this application.
///
/// Context values are passed through Aegis's redaction model rather than
/// formatted into a string, so a value that should not be recorded is not
/// recorded (VIG-SEC-005, VIG-PER-032).
let scopeFor (config: AegisConfig) (operation: OperationName) context =
    Aegis.scope config operation context
