/// The durable `followup.create` ledger (VIG-15).
///
/// `MemoryFiles` models the repository with GitHub's contents-API semantics:
/// a create-only write is atomic and refused when the file exists. It is
/// shared between ledger instances to model a restart -- the ledger itself
/// holds nothing, so a new instance over the same files is a new process over
/// the same repository.
///
/// Requirements: VIG-AGT-012, VIG-AGT-015, VIG-AGT-034, VIG-PER-005,
/// VIG-PER-021, VIG-PER-030.
module Vigila.Host.GitHub.FollowUpLedgerTests

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open Aegis
open Xunit
open Vigila.Semantic.Identifiers
open Vigila.Semantic.Items
open Vigila.Semantic.Time
open Vigila.Application.Integration
open Vigila.Host.GitHub.StorageLayout
open Vigila.Host.GitHub.GitHubStore
open Vigila.Host.GitHub.FollowUpLedger

type private MemoryFiles() =
    let files = ConcurrentDictionary<string, string>()

    member val FailCreatesUnder: string option = None with get, set
    member _.Paths = files.Keys |> Seq.sort |> Seq.toList
    member _.Get path = files.TryGetValue path |> function | true, c -> Some c | _ -> None
    member _.Put(path, content) = files[path] <- content
    member _.Remove(path: string) = files.TryRemove path |> ignore

    interface RepositoryFiles with
        member _.Read path =
            match files.TryGetValue path with
            | true, content -> Ok(Some content)
            | _ -> Ok None

        member this.CreateNew(path, content) =
            match this.FailCreatesUnder with
            | Some prefix when path.Contains prefix -> Error RepositoryUnavailable
            | _ -> if files.TryAdd(path, content) then Ok FileCreated else Ok FileAlreadyExists

let private aegis =
    match Bootstrap.validate None (Aegis.configure "Vigila" None [ Sinks.standardError ]) with
    | Ok valid -> valid
    | Result.Error problems -> failwith $"%A{problems}"

let private root =
    match StoragePath.create "vigila" with
    | Ok p -> p
    | Error e -> failwith e.Describe

let private workspace =
    match WorkspaceId.parse "3f2a1c4e-0000-4000-8000-000000000001" with
    | Ok w -> w
    | Error e -> failwith e

let private instant = Instant.ofDateTimeOffset (DateTimeOffset(2026, 9, 25, 15, 0, 0, TimeSpan.Zero))
let private clock = Clock.fixedAt instant

let private request operationId =
    { Envelope =
        { OperationId = operationId
          CorrelationId = "corr-15"
          Timestamp = instant
          Actor =
            { Kind = AgentKind
              Provider = Known "openai"
              Identity = Known "gpt-5.6-sol"
              RunId = Known "run-15"
              SessionId = Unknown }
          Source =
            Some
                { Repository = Known "kemiller2002/vigila"
                  Branch = Known "feature/echelon-followup-integration-15"
                  Commit = NotApplicable
                  WorkItem = Known "15" } }
      FollowUp =
        { Title = "Review integration result"
          Reason = "Agent discovered a decision that needs human review."
          RequestedAction = Review
          Priority = Urgent
          ReviewAfter = Some(Instant.ofDateTimeOffset (DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero)))
          DueAt = Some(Instant.ofDateTimeOffset (DateTimeOffset(2026, 10, 15, 17, 0, 0, TimeSpan.Zero)))
          Tags = [ "integration"; "review" ]
          Context = Some """{"k":"v"}""" } }

let private ledgerOver files =
    Vigila.Host.GitHub.FollowUpLedger.create files root workspace

let private run files r = Vigila.Application.Integration.create aegis clock (ledgerOver files) r

let private itemFiles (files: MemoryFiles) =
    files.Paths |> List.filter (fun p -> p.StartsWith(itemsPath root workspace + "/", StringComparison.Ordinal))

let private operationFiles (files: MemoryFiles) =
    files.Paths |> List.filter (fun p -> p.StartsWith(operationsPath root workspace + "/", StringComparison.Ordinal))

let private created =
    function
    | Created r -> r
    | other -> failwith $"Expected Created, got %A{other}"

// ---------------------------------------------------------------------------
// Layout
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("op-1")>]
[<InlineData("../../escape")>]
[<InlineData("a/b\\c:d")>]
[<InlineData("日本語")>]
let ``an operation path is deterministic, hashed and inside the storage area`` (operationId: string) =
    let path = operationPath root workspace operationId

    Assert.Equal(path, operationPath root workspace operationId)
    Assert.True(isInside root path)
    Assert.Matches($"^%s{operationsPath root workspace}/[0-9a-f]{{64}}\\.json$", path)

[<Fact>]
let ``different operation ids name different files`` () =
    Assert.NotEqual<string>(operationPath root workspace "op-1", operationPath root workspace "op-2")

// ---------------------------------------------------------------------------
// Durability
// ---------------------------------------------------------------------------

[<Fact>]
let ``creating writes one operation record and one item file`` () =
    let files = MemoryFiles()
    let record = run files (request "op-1") |> created

    Assert.Equal<string list>([ operationPath root workspace "op-1" ], operationFiles files)
    Assert.Equal<string list>([ itemPath root workspace record.Item.Id ], itemFiles files)

[<Fact>]
let ``the stored item is the mapped follow-up`` () =
    let files = MemoryFiles()
    let record = run files (request "op-item") |> created

    match files.Get(itemPath root workspace record.Item.Id) |> Option.map ItemJson.fromJson with
    | Some(Ok item) ->
        Assert.Equal(ItemKind.FollowUp, item.Kind)
        Assert.Equal(record.Item.Due, item.Due)
        Assert.Equal(record.Item.FollowUp, item.FollowUp)
        Assert.True(item.Important)
        Assert.True(item.NeedsReview)
    | other -> failwith $"%A{other}"

[<Fact>]
let ``a replay after a restart finds the original item`` () =
    // Two ledgers over one repository: nothing about the first survives in
    // memory, so the second can only answer from what was persisted.
    let files = MemoryFiles()
    let original = run files (request "op-restart") |> created

    match Vigila.Application.Integration.create aegis clock (ledgerOver files) (request "op-restart") with
    | Replayed replayed ->
        Assert.Equal(original.Item.Id, replayed.Item.Id)
        Assert.Single(itemFiles files) |> ignore
        Assert.Single(operationFiles files) |> ignore
    | other -> failwith $"Expected Replayed, got %A{other}"

[<Fact>]
let ``the operation record round-trips every provenance and contract field`` () =
    let files = MemoryFiles()
    let record = run files (request "op-roundtrip") |> created

    match files.Get(operationPath root workspace "op-roundtrip") |> Option.map OperationJson.fromJson with
    | Some(Ok read) ->
        Assert.Equal(record.Envelope, read.Envelope)
        Assert.Equal(record.FollowUp, read.FollowUp)
        Assert.Equal(record.Item.Id, read.Item.Id)
        Assert.Equal(Urgent, read.FollowUp.Priority)
        Assert.Equal(Known "run-15", read.Envelope.Actor.RunId)
        Assert.Equal(Some """{"k":"v"}""", read.FollowUp.Context)
    | other -> failwith $"%A{other}"

[<Fact>]
let ``the operation record carries no credential or transport detail`` () =
    let files = MemoryFiles()
    run files (request "op-safe") |> created |> ignore
    let json = files.Get(operationPath root workspace "op-safe") |> Option.defaultValue ""

    for forbidden in [ "token"; "authorization"; "api.github.com"; "sha\"" ] do
        Assert.DoesNotContain(forbidden, json.ToLowerInvariant())

// ---------------------------------------------------------------------------
// Concurrency
// ---------------------------------------------------------------------------

[<Fact>]
let ``simultaneous invocations through separate ledgers create exactly one item`` () =
    // Separate ledger instances model separate processes; the only shared
    // thing is the repository, as in production.
    let files = MemoryFiles()

    let outcomes =
        Array.init 64 (fun _ -> Task.Run(Func<CreateOutcome>(fun () -> run files (request "op-race"))))
        |> Task.WhenAll
        |> fun t -> t.Result

    let createdCount = outcomes |> Array.filter (function Created _ -> true | _ -> false) |> Array.length

    let ids =
        outcomes
        |> Array.choose (function
            | Created r
            | Replayed r -> Some(ItemId.toGuid r.Item.Id)
            | _ -> None)

    Assert.Equal(1, createdCount)
    Assert.Equal(64, ids.Length)
    Assert.Single(Array.distinct ids) |> ignore
    Assert.Single(itemFiles files) |> ignore
    Assert.Single(operationFiles files) |> ignore

// ---------------------------------------------------------------------------
// Failure and recovery
// ---------------------------------------------------------------------------

[<Fact>]
let ``a failed claim reports failure and writes nothing`` () =
    let files = MemoryFiles(FailCreatesUnder = Some "/operations/")

    match run files (request "op-down") with
    | Failed fault ->
        Assert.Equal(FaultCode "VIGILA.INTEGRATION.REPOSITORYUNAVAILABLE", fault.Code)
        Assert.Empty(files.Paths)
    | other -> failwith $"Expected Failed, got %A{other}"

[<Fact>]
let ``a failure after the claim is reported as failure and a replay completes it`` () =
    // The operation record is written, then the item write fails. The first
    // call must not claim success; the replay must not create a second item.
    let files = MemoryFiles(FailCreatesUnder = Some "/items/")

    match run files (request "op-half") with
    | Failed _ -> Assert.Empty(itemFiles files)
    | other -> failwith $"Expected Failed, got %A{other}"

    files.FailCreatesUnder <- None

    match run files (request "op-half") with
    | Replayed record ->
        Assert.Equal<string list>([ itemPath root workspace record.Item.Id ], itemFiles files)
        Assert.Single(operationFiles files) |> ignore
    | other -> failwith $"Expected Replayed, got %A{other}"

[<Fact>]
let ``a replay never overwrites an item edited since creation`` () =
    let files = MemoryFiles()
    let record = run files (request "op-edited") |> created
    let path = itemPath root workspace record.Item.Id
    let edited = (files.Get path |> Option.defaultValue "").Replace("Review integration result", "Edited by the user")
    files.Put(path, edited)

    match run files (request "op-edited") with
    | Replayed _ -> Assert.Equal(Some edited, files.Get path)
    | other -> failwith $"Expected Replayed, got %A{other}"

[<Fact>]
let ``a corrupt operation record is reported, not repaired or replaced`` () =
    let files = MemoryFiles()
    let path = operationPath root workspace "op-corrupt"
    files.Put(path, "{ not json")

    match run files (request "op-corrupt") with
    | Failed fault ->
        Assert.Equal(FaultCode "VIGILA.INTEGRATION.STORAGECORRUPT", fault.Code)
        Assert.Equal(Some "{ not json", files.Get path)
        Assert.Empty(itemFiles files)
    | other -> failwith $"Expected Failed, got %A{other}"

[<Fact>]
let ``an operation record from a newer schema is refused`` () =
    let files = MemoryFiles()
    let record = run files (request "op-schema") |> created
    let path = operationPath root workspace "op-schema"
    let json = (files.Get path |> Option.defaultValue "").Replace("\"schemaVersion\": 1,\n  \"capability\"", "\"schemaVersion\": 2,\n  \"capability\"")

    match OperationJson.fromJson json with
    | Error message -> Assert.Contains("schema version 2", message)
    | Ok _ -> failwith $"A version-2 record for %A{record.Item.Id} should be refused."

[<Fact>]
let ``a conflicting reuse is detected from the persisted record`` () =
    let files = MemoryFiles()
    run files (request "op-conflict") |> created |> ignore

    let different =
        { request "op-conflict" with
            FollowUp = { (request "op-conflict").FollowUp with Priority = Low } }

    match run files different with
    | Conflicted _ -> Assert.Single(itemFiles files) |> ignore
    | other -> failwith $"Expected Conflicted, got %A{other}"

[<Fact>]
let ``GitHub failures keep their stable code and retryability`` () =
    let limited = failureOf (RateLimited(Some 30))
    let corrupt = failureOf (StorageCorrupt "x")

    Assert.Equal("RateLimited", limited.Code)
    Assert.True(limited.Retryable)
    Assert.Equal("StorageCorrupt", corrupt.Code)
    Assert.False(corrupt.Retryable)
