/// Tier 4 - Host / External Effects.
///
/// Owns the GitHub conversation. This is the only tier permitted to know that
/// persistence is a Git repository at all (VIG-PER-001).
///
/// Scaffold: the port is wired and the failure classification is stated, but
/// no GitHub call is implemented yet. Implementing it is separate work,
/// governed by the persistence requirements.
module Vigila.Host.GitHub.GitHubStore

open Aegis

/// Classifies a GitHub failure into the vocabulary Vigila's callers use.
///
/// The mapping is deliberately explicit rather than inferred from an exception
/// type, because VIG-AGT-050 requires stable machine-readable codes and
/// VIG-SEC-015 requires a protected-branch refusal to be distinguishable from
/// a generic save failure.
///
/// Requirements: VIG-AGT-050, VIG-AGT-051, VIG-PER-046, VIG-SEC-012,
/// VIG-SEC-015.
type GitHubFailure =
    /// Token is absent, malformed, expired or revoked (VIG-SEC-012).
    | Unauthorized
    /// Token is valid but lacks the required capability (VIG-SEC-012).
    | Forbidden
    /// Repository does not exist, or is invisible to this token. GitHub does
    /// not always distinguish these, and VIG-SEC-012 says not to depend on it
    /// doing so.
    | RepositoryNotFound
    /// The configured branch is missing or refuses the write, including
    /// protected-branch rules (VIG-SEC-014, VIG-SEC-015).
    | BranchUnavailable
    /// GitHub's rate limit was hit. Retryable, and never a successful save
    /// (VIG-PER-046).
    | RateLimited of retryAfterSeconds: int option
    /// Reachability or transport failure. Retryable.
    | RepositoryUnavailable
    /// A stored record could not be parsed. Not retryable, and never silently
    /// repaired (VIG-PER-030, VIG-PER-031).
    | StorageCorrupt of path: string

    /// Whether a caller may retry without changing the request or the state
    /// (VIG-AGT-051). A conflict is absent on purpose: retrying a conflict
    /// without re-reading is exactly what VIG-AGT-031 forbids.
    member this.IsRetryable =
        match this with
        | RateLimited _
        | RepositoryUnavailable -> true
        | Unauthorized
        | Forbidden
        | RepositoryNotFound
        | BranchUnavailable
        | StorageCorrupt _ -> false

/// The stable code a caller branches on, matching the taxonomy in
/// VIG-AGT-050. Kept separate from the case name so renaming a case cannot
/// silently change the wire contract.
let codeOf failure =
    match failure with
    | Unauthorized -> "Unauthorized"
    | Forbidden -> "Forbidden"
    | RepositoryNotFound -> "RepositoryNotFound"
    | BranchUnavailable -> "BranchUnavailable"
    | RateLimited _ -> "RateLimited"
    | RepositoryUnavailable -> "RepositoryUnavailable"
    | StorageCorrupt _ -> "StorageCorrupt"
