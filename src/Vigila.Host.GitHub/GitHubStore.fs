/// Tier 4 - Host / External Effects.
///
/// Owns the GitHub conversation. This is the only tier permitted to know that
/// persistence is a Git repository at all (VIG-PER-001).
///
/// The port and the failure vocabulary live here; the HTTP transport is
/// `GitHubRepositoryFiles` and bounded retry is `RetryingRepositoryFiles`.
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
    /// Reachability or transport failure, a 5xx response, or 408 Request
    /// Timeout. Retryable.
    | RepositoryUnavailable
    /// Any other 4xx response (for example 400 Bad Request or 410 Gone). The
    /// request itself was refused, so repeating it unchanged cannot succeed.
    /// Not retryable.
    | RequestRejected of status: int
    /// A status outside the classified ranges (for example an unfollowed 3xx).
    /// Not retryable: retrying something not understood is a blind retry.
    | UnexpectedStatus of status: int
    /// A retryable failure persisted for every attempt the retry policy
    /// allows. Terminal: the bounded retry already happened, so the caller
    /// receives an explicit end state rather than an invitation to loop.
    | RetriesExhausted of attempts: int * last: GitHubFailure
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
        | RequestRejected _
        | UnexpectedStatus _
        | RetriesExhausted _
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
    | RequestRejected _ -> "RequestRejected"
    | UnexpectedStatus _ -> "UnexpectedStatus"
    | RetriesExhausted _ -> "RetriesExhausted"
    | StorageCorrupt _ -> "StorageCorrupt"

/// Classifies an unsuccessful HTTP status. Pure and total, so every status
/// class is explicit and table-testable rather than falling into a default.
///
/// - 429, or 403 with an exhausted rate limit: `RateLimited` (retryable).
/// - 401, 403, 404, 409/422: their specific non-retryable cases.
/// - 408 Request Timeout and every 5xx: `RepositoryUnavailable` (retryable).
/// - every other 4xx: `RequestRejected` (terminal).
/// - anything else: `UnexpectedStatus` (terminal).
let classifyStatus (status: int) (rateLimitExhausted: bool) (retryAfterSeconds: int option) =
    match status with
    | 429 -> RateLimited retryAfterSeconds
    | 403 when rateLimitExhausted -> RateLimited retryAfterSeconds
    | 401 -> Unauthorized
    | 403 -> Forbidden
    | 404 -> RepositoryNotFound
    | 409
    | 422 -> BranchUnavailable
    | 408 -> RepositoryUnavailable
    | s when s >= 500 && s <= 599 -> RepositoryUnavailable
    | s when s >= 400 && s <= 499 -> RequestRejected s
    | s -> UnexpectedStatus s


/// The outcome of a create-only repository write.
type FileWrite =
    | FileCreated
    | FileAlreadyExists

/// Minimal repository file port used by durable persistence. CreateNew is an
/// atomic create-if-absent operation; repository identity, branch and
/// credentials belong to the implementation.
type RepositoryFiles =
    abstract Read: path: string -> Result<string option, GitHubFailure>
    abstract CreateNew: path: string * content: string -> Result<FileWrite, GitHubFailure>
