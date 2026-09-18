/// Tier 3 - the connection to the configured data repository.
///
/// This module owns two things and deliberately not a third:
///
///   * the *shape* of a connection configuration — which repository, which
///     branch — and the rules for what counts as a valid one; and
///   * the connection state machine VIG-SEC-007 describes.
///
/// It does not own the token. The token never enters the engine at all: it is
/// held kernel-side, written to `localStorage` by browser code, and attached to
/// outbound requests by the transport. The engine learns one bit about it —
/// whether one is present — and nothing more.
///
/// That is a structural reading of VIG-SEC-005 rather than a disciplined one.
/// The requirement lists thirteen places the token must never appear (logs,
/// receipts, command envelopes, telemetry, pagination cursors, exports, and so
/// on). Every one of those is produced on this side of the boundary, so a token
/// that is never here cannot leak into any of them, and no future code path has
/// to remember not to. VIG-SEC-020 says the same thing in the other direction:
/// domain code must not know about GitHub tokens.
///
/// Requirements: VIG-SEC-001, VIG-SEC-005, VIG-SEC-007, VIG-SEC-008,
/// VIG-SEC-009, VIG-SEC-012, VIG-SEC-020.
module Vigila.Application.Connection

open System

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------

/// A repository in `owner/name` form (VIG-SEC-001).
///
/// Private constructor: an instance cannot exist unless it parsed, so no code
/// downstream has to re-check the shape or decide what a malformed one means.
[<CustomEquality; NoComparison>]
type RepositoryId =
    private
        { Owner: string
          Name: string }

    member this.Value = $"%s{this.Owner}/%s{this.Name}"

    override this.Equals other =
        match other with
        | :? RepositoryId as r -> r.Owner = this.Owner && r.Name = this.Name
        | _ -> false

    override this.GetHashCode() =
        let owner, name = this.Owner, this.Name
        HashCode.Combine(owner, name)

/// The branch Vigila writes to, defaulting to `main` (VIG-SEC-001).
[<CustomEquality; NoComparison>]
type BranchName =
    private
        | BranchName of string

    member this.Value =
        let (BranchName value) = this
        value

    override this.Equals other =
        match other with
        | :? BranchName as b -> b.Value = this.Value
        | _ -> false

    override this.GetHashCode() =
        let (BranchName value) = this
        value.GetHashCode()

/// Why a configuration value was refused.
///
/// A closed set rather than a message, so the UI can say something specific and
/// a test can assert on the reason rather than on prose.
type ConfigurationRefusal =
    | RepositoryMissing
    | RepositoryNotOwnerSlashName
    | RepositoryPartBlank
    | BranchMissing
    | BranchNotAllowed

let private refusalMessage =
    function
    | RepositoryMissing -> "Enter a repository."
    | RepositoryNotOwnerSlashName -> "A repository looks like owner/repository."
    | RepositoryPartBlank -> "Both the owner and the repository name are required."
    | BranchMissing -> "Enter a branch, or leave it as main."
    | BranchNotAllowed -> "That is not a usable branch name."

/// The text shown for a refusal. Kept beside the cases so adding a case fails
/// to compile until it has one.
let describeRefusal = refusalMessage

module RepositoryId =

    /// `owner/name`, both non-blank, and exactly one separator.
    ///
    /// Surrounding whitespace is trimmed because a pasted value routinely
    /// carries it; interior whitespace is not, because that is a typo rather
    /// than a formatting artefact.
    let create (raw: string | null) =
        match raw with
        | Null -> Error RepositoryMissing
        | NonNull text ->
            let trimmed = text.Trim()

            if String.IsNullOrWhiteSpace trimmed then
                Error RepositoryMissing
            else
                match trimmed.Split '/' with
                | [| owner; name |] ->
                    if String.IsNullOrWhiteSpace owner || String.IsNullOrWhiteSpace name then
                        Error RepositoryPartBlank
                    else
                        Ok { Owner = owner; Name = name }
                | _ -> Error RepositoryNotOwnerSlashName

    let value (repository: RepositoryId) = repository.Value
    let owner (repository: RepositoryId) = repository.Owner
    let name (repository: RepositoryId) = repository.Name

module BranchName =

    [<Literal>]
    let Default = "main"

    /// Refuses the shapes Git itself refuses, so a doomed request is not sent.
    ///
    /// This is not a complete implementation of git-check-ref-format; it is the
    /// subset that a person typing into a form can plausibly produce.
    let create (raw: string | null) =
        let forbidden = [| ' '; '~'; '^'; ':'; '?'; '*'; '['; '\\' |]

        match raw with
        | Null -> Error BranchMissing
        | NonNull text ->
            let trimmed = text.Trim()

            if String.IsNullOrWhiteSpace trimmed then
                Error BranchMissing
            elif trimmed.IndexOfAny forbidden >= 0
                 || trimmed.StartsWith "/"
                 || trimmed.EndsWith "/"
                 || trimmed.EndsWith "."
                 || trimmed.Contains ".."
                 || trimmed.StartsWith "-" then
                Error BranchNotAllowed
            else
                Ok(BranchName trimmed)

    /// The branch used when the user leaves the field alone (VIG-SEC-001).
    let fallback =
        match create Default with
        | Ok branch -> branch
        | Error _ -> failwith "The default branch name must be valid."

    let value (branch: BranchName) = branch.Value

/// Everything the engine knows about where data lives. Note what is absent.
[<NoComparison>]
type ConnectionSettings =
    { Repository: RepositoryId
      Branch: BranchName }

// ---------------------------------------------------------------------------
// The connection state machine (VIG-SEC-007)
// ---------------------------------------------------------------------------

/// Why a connection attempt was refused.
///
/// These are the codes VIG-AGT-050 fixes, and VIG-SEC-012 requires each to be
/// explicit rather than collapsed into a generic failure. `WriteAccessMissing`
/// is separate from `Forbidden` because VIG-SEC-009 is explicit that a
/// successful read must not be taken to imply a successful write.
type ConnectionRefusal =
    | TokenMissing
    | Unauthorized
    | Forbidden
    | RepositoryNotFound
    | BranchUnavailable
    | WriteAccessMissing
    | RepositoryUnavailable

/// The stable machine-readable code for a refusal (VIG-AGT-050).
///
/// Kept separate from the case names so renaming a case cannot silently change
/// what a caller branches on.
let refusalCode =
    function
    | TokenMissing -> "TokenMissing"
    | Unauthorized -> "Unauthorized"
    | Forbidden -> "Forbidden"
    | RepositoryNotFound -> "RepositoryNotFound"
    | BranchUnavailable -> "BranchUnavailable"
    | WriteAccessMissing -> "WriteAccessMissing"
    | RepositoryUnavailable -> "RepositoryUnavailable"

/// What the user is told. VIG-SEC-012 requires the distinction between an
/// invalid token and an inaccessible repository to survive to the surface.
let describeConnectionRefusal =
    function
    | TokenMissing -> "Enter a GitHub token to connect."
    | Unauthorized -> "GitHub rejected that token. It may be invalid, expired, or revoked."
    | Forbidden -> "That token cannot access this repository. It may be missing the repo scope."
    | RepositoryNotFound ->
        "That repository was not found. It may not exist, or the token may not be allowed to see it."
    | BranchUnavailable -> "That branch was not found on the repository."
    | WriteAccessMissing -> "That token can read the repository but cannot write to it, so Vigila cannot save."
    | RepositoryUnavailable -> "GitHub could not be reached. The connection was not established."

/// Whether trying again without changing anything could plausibly succeed
/// (VIG-AGT-051).
let isRetryable =
    function
    | RepositoryUnavailable -> true
    | TokenMissing
    | Unauthorized
    | Forbidden
    | RepositoryNotFound
    | BranchUnavailable
    | WriteAccessMissing -> false

/// Which capability is being proved (VIG-SEC-009).
///
/// The two steps are separate because a repository that reads successfully
/// says nothing about whether it can be written to, and VIG-SEC-009 forbids
/// inferring one from the other.
type ValidationStep =
    | ProvingRead
    | ProvingWrite

/// Where the application is in VIG-SEC-007's sequence.
///
/// `Refused` carries the reason rather than a flag, so no code path can report
/// a failure it cannot name, and VIG-SEC-012's "must not present stale data as
/// though the connection succeeded" is enforced by there being no state that
/// means both.
type ConnectionStatus =
    | Unconfigured
    | Validating of ValidationStep
    | Connected
    | Refused of ConnectionRefusal

[<NoComparison>]
type Connection =
    { Settings: ConnectionSettings option
      HasToken: bool
      Status: ConnectionStatus }

let disconnected =
    { Settings = None
      HasToken = false
      Status = Unconfigured }

/// Whether the setup screen is what the user should be looking at.
///
/// True for everything except a live connection, which is what VIG-SEC-007
/// steps 2 and 7 require: a failed validation returns the user to configuration
/// rather than into the application.
let needsSetup connection = connection.Status <> Connected

// ---------------------------------------------------------------------------
// Persisted keys
// ---------------------------------------------------------------------------

/// The `localStorage` keys the engine reads and writes (VIG-SEC-002).
///
/// The token's own key is deliberately absent: the engine never names it,
/// because naming it is the first step towards reading it. `TokenPresent` is a
/// flag the kernel maintains beside the token, and it is the whole of what the
/// engine is told.
module Keys =
    [<Literal>]
    let Repository = "vigila.repository"

    [<Literal>]
    let Branch = "vigila.branch"

    [<Literal>]
    let TokenPresent = "vigila.tokenPresent"

    let all = [ Repository; Branch; TokenPresent ]

// ---------------------------------------------------------------------------
// Validation requests (VIG-SEC-009)
// ---------------------------------------------------------------------------

[<Literal>]
let private ApiRoot = "https://api.github.com"

/// How long a validation request may take before the kernel gives up.
[<Literal>]
let TimeoutMs = 15000

/// The request that proves the repository is reachable and readable, and — from
/// the same response — whether the credential may write.
///
/// One request answers both because GitHub returns `permissions.push` on the
/// repository resource for an authenticated caller. VIG-SEC-010 asks that
/// capability be determined safely rather than by a probe write where possible,
/// and this is that safer means.
let readProbe (settings: ConnectionSettings) =
    { CorrelationId = ""
      Method = Effects.Get
      Url = $"%s{ApiRoot}/repos/%s{RepositoryId.value settings.Repository}"
      Headers = []
      Body = None
      TimeoutMs = TimeoutMs }
    : Effects.HttpEffect

/// The request that proves the configured branch exists (VIG-SEC-009,
/// VIG-SEC-014).
let branchProbe (settings: ConnectionSettings) =
    { CorrelationId = ""
      Method = Effects.Get
      Url =
        $"%s{ApiRoot}/repos/%s{RepositoryId.value settings.Repository}/branches/%s{BranchName.value settings.Branch}"
      Headers = []
      Body = None
      TimeoutMs = TimeoutMs }
    : Effects.HttpEffect

/// Classifies an HTTP status from a validation request.
///
/// `notFound` differs between the two probes: a 404 from the repository request
/// means the repository is missing or invisible, and a 404 from the branch
/// request means the branch is. VIG-SEC-015 exists precisely so these do not
/// collapse into one generic failure.
let classifyStatus notFound status =
    match status with
    | 200 -> Ok()
    | 401 -> Error Unauthorized
    | 403 -> Error Forbidden
    | 404 -> Error notFound
    | _ -> Error RepositoryUnavailable

/// Whether the repository response says this credential may push.
///
/// Absent `permissions` means the caller is not authenticated as someone with
/// declared permissions, which VIG-SEC-009 says to treat as "no write" rather
/// than as "probably fine".
let classifyWriteCapability canPush =
    if canPush then Ok() else Error WriteAccessMissing
