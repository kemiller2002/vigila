/// Tier 4 - the durable, concurrency-safe `followup.create` ledger.
///
/// Implements the application's FollowUpLedger port over the repository's
/// files. Idempotency rests on one primitive: a create-only write of the
/// operation record at a path derived from the operation id. Whoever creates
/// that file owns the operation; everyone else reads it. Because the claim is
/// the store's own atomic operation, two simultaneous invocations cannot both
/// win, and because it is in the repository rather than in memory, a restarted
/// process reaches the same answer.
///
/// Order of writes: the operation record first, then the item file. A failure
/// between the two leaves a claimed operation whose item file is missing; the
/// invocation reports failure (never success, VIG-AGT-015), and any replay of
/// the same operation id writes the item from the record. The item write is
/// itself create-only, so a repair can never overwrite an item that has since
/// been edited.
///
/// Requirements: VIG-AGT-012, VIG-AGT-013, VIG-AGT-015, VIG-AGT-034,
/// VIG-PER-001, VIG-PER-005.
module Vigila.Host.GitHub.FollowUpLedger

open Vigila.Semantic.Identifiers
open Vigila.Application.Integration
open Vigila.Host.GitHub.StorageLayout
open Vigila.Host.GitHub.GitHubStore

/// The outcome of a create-only write.
type FileWrite =
    | FileCreated
    | FileAlreadyExists

/// The repository file operations the ledger needs, as Tier 4 sees them.
///
/// `CreateNew` MUST be atomic: it writes only if no file exists at the path,
/// and of concurrent callers exactly one observes FileCreated. GitHub's
/// contents API provides this -- a PUT without a blob SHA is refused when the
/// file already exists -- and so does any store with create-if-absent.
///
/// Paths are repository-relative paths inside the configured storage area.
/// Credentials, branch and repository identity belong to the implementation,
/// never to this interface (VIG-SEC-020).
type RepositoryFiles =
    abstract Read: path: string -> Result<string option, GitHubFailure>
    abstract CreateNew: path: string * content: string -> Result<FileWrite, GitHubFailure>

/// A GitHub failure in the ledger's vocabulary. Carries the stable code only:
/// no token, URL or response body (VIG-AGT-013).
let failureOf (failure: GitHubFailure) =
    { Code = codeOf failure
      Retryable = failure.IsRetryable
      Detail = codeOf failure }

let private corrupt (detail: string) =
    { Code = codeOf (StorageCorrupt "operation record")
      Retryable = false
      Detail = detail }

/// A claimed operation whose record cannot be read yet. Retryable: the claim
/// exists, so a retry cannot create a second item.
let private unreadable =
    { Code = "PersistenceFailed"
      Retryable = true
      Detail = "The operation is claimed but its record could not be read." }

let private ensureItem (files: RepositoryFiles) root workspace (record: FollowUpRecord) =
    match files.CreateNew(itemPath root workspace record.Item.Id, ItemJson.toJson record.Item) with
    | Ok FileCreated
    | Ok FileAlreadyExists -> Ok()
    | Error failure -> Error(failureOf failure)

let private existing (files: RepositoryFiles) root workspace path =
    match files.Read path with
    | Error failure -> Error(failureOf failure)
    | Ok None -> Error unreadable
    | Ok(Some json) ->
        match OperationJson.fromJson json with
        | Error detail -> Error(corrupt detail)
        | Ok record -> ensureItem files root workspace record |> Result.map (fun () -> AlreadyRecorded record)

let private recordIn (files: RepositoryFiles) root workspace (record: FollowUpRecord) =
    let path = operationPath root workspace record.Envelope.OperationId

    match files.CreateNew(path, OperationJson.toJson record) with
    | Ok FileCreated -> ensureItem files root workspace record |> Result.map (fun () -> Recorded)
    | Ok FileAlreadyExists -> existing files root workspace path
    | Error failure -> Error(failureOf failure)

/// A ledger for one workspace. Holds no state of its own: every answer comes
/// from the repository files, which is what makes it survive a restart.
let create (files: RepositoryFiles) (root: StoragePath) (workspace: WorkspaceId) =
    { new FollowUpLedger with
        member _.Record record = recordIn files root workspace record }
