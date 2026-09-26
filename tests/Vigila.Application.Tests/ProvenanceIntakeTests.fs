/// `followup.create` intake and attributed operations: every role keeps its
/// own identity, keyed by its own execution (VIG-PROV-002..VIG-PROV-016).
module Vigila.Application.ProvenanceIntakeTests

open Xunit
open Vigila.Semantic.Time
open Vigila.Semantic.Actors
open Vigila.Semantic.Items
open Vigila.Semantic.Item
open Vigila.Semantic.Provenance
open Vigila.Application
open Vigila.Application.Attribution
open Vigila.Application.FollowUpIntake

let gemini = ProvenanceActor.agent "google/gemini-cli" "google" "unknown" "gemini-cli"
let codex = ProvenanceActor.agent "openai/codex" "openai" "gpt-5-codex" "codex"
let claude = ProvenanceActor.agent "anthropic/claude-code" "anthropic" "unknown" "claude-code"
let aegis = ProvenanceActor.automation "echelon/aegis" "echelon" "aegis"

/// The provenance an Aegis finding carries: agent A (Gemini) discovered it.
/// `x-severity` and `subject` are fields this version does not model.
let findingProvenance =
    """{"schema":"praxis.provenance/1",
        "subject":{"id":"aegis:finding/SF-0001"},
        "contributions":{"EXE-20260926T090000000Z-c1c1c1c1":{
            "operations":["created","discovered"],"at":"2026-09-26T09:00:00.000Z",
            "actor":{"kind":"agent","id":"google/gemini-cli","provider":"google","model":"unknown","runtime":"gemini-cli"},
            "reason":"Aegis review discovers an injection risk","evidence":["aegis:evidence/EVD-0001"],"x-severity":"high"}},
        "derivedFrom":["git:commit/5e1f0c2","dokimos:observation/OBS-2026-0001"]}"""

let actorJson (actor: ProvenanceActor) = (ProvenanceJson.actorToNode actor).ToJsonString()

let envelope operationId (actor: ProvenanceActor) (execution: string option) (provenance: string option) =
    let executionPart =
        match execution with
        | Some key -> $""","execution":"%s{key}" """
        | None -> ""

    let provenancePart =
        match provenance with
        | Some block -> $""","provenance":%s{block}"""
        | None -> ""

    $"""{{"schema":"echelon.execution-envelope/v2","operationId":"%s{operationId}","correlationId":"corr-%s{operationId}",
         "timestamp":"2026-09-26T09:05:00.000Z","actor":%s{actorJson actor}%s{executionPart}%s{provenancePart},"x-trace":"t-1"}}"""

let payload =
    """{"title":"Fix the injection risk in the query builder","reason":"Aegis finding SF-0001 needs remediation.",
        "requestedAction":"investigate","priority":"high","reviewAfter":"2026-09-27T09:00:00Z","dueAt":null,
        "tags":["security","aegis"],
        "context":{"source":{"ref":"aegis:finding/SF-0001","url":"https://aegis.example/findings/SF-0001"}}}"""

let noneStored _ = None

let received result =
    match result with
    | Ok intake -> intake
    | Error(e: IntakeError) -> failwith $"%s{e.Code}: %A{e.Problems}"

let ownBlock (item: Item) =
    match item.Provenance with
    | Some(Recorded block) -> block
    | other -> failwith $"expected recorded provenance, got %A{other}"

let receivedBlock (item: Item) =
    match item.ReceivedProvenance with
    | Some(Recorded block) -> block
    | other -> failwith $"expected recorded received provenance, got %A{other}"

let keysWith operation block = Provenance.withRole operation block |> List.map fst

let instant text =
    match Instant.parse text with
    | Ok value -> value
    | Error e -> failwith e

let attribution actor execution operationId at =
    { Actor = actor
      Execution = execution
      OperationId = operationId
      At = instant at
      Reason = None }

let ok result =
    match result with
    | Ok value -> value
    | Error(e: AttributionError) -> failwith $"%A{e}"

let aegisIntake () =
    receive
        defaultOptions
        noneStored
        (envelope "op-aegis-0001" aegis (Some "EXT-aegis.review-20260926-01") (Some findingProvenance))
        payload
    |> received

[<Fact>]
let ``a generating system is the follow-up's creator; the discoverer stays in the received provenance`` () =
    let intake = aegisIntake ()
    let item = intake.Item
    let own = ownBlock item

    // The generating system created the follow-up...
    match Provenance.originator own with
    | Some(key, entry) ->
        Assert.Equal("EXT-aegis.review-20260926-01", key)
        Assert.Equal(aegis, entry.Actor)
    | None -> failwith "no originator"

    // ...Vigila transformed the request into an item...
    Assert.Equal<string list>([ "EXT-vigila.op-aegis-0001" ], keysWith Operation.Transformed own)
    Assert.Equal(ProvenanceActor.vigila, (own.Contributions |> List.find (fun (k, _) -> k = "EXT-vigila.op-aegis-0001") |> snd).Actor)

    // ...and the agent that discovered the finding is not an author of the
    // follow-up: it is kept, verbatim, as where the follow-up came from.
    Assert.DoesNotContain(own.Contributions, fun (_, entry) -> entry.Actor = gemini)
    let upstream = receivedBlock item
    Assert.Equal<string list>([ "EXE-20260926T090000000Z-c1c1c1c1" ], keysWith Operation.Discovered upstream)
    Assert.Equal(gemini, (snd upstream.Contributions.Head).Actor)

    // Lineage, not authorship: upstream lineage plus the source reference.
    Assert.Equal(
        Some [ "git:commit/5e1f0c2"; "dokimos:observation/OBS-2026-0001"; "aegis:finding/SF-0001" ],
        own.DerivedFrom
    )

    // The legacy fields are the projection of the invoking actor.
    Assert.Equal({ Type = AutomatedProcess; Name = "echelon/aegis" }, item.CreatedBy)
    Assert.Equal(CreatedVia.Integration, item.CreatedVia)
    Assert.Equal(Some "EXT-aegis.review-20260926-01", item.History.Head.Contribution)
    Assert.Equal(FollowUp, item.Kind)
    Assert.True item.Important
    Assert.Equal(Some "Investigate", item.NextAction)
    Assert.Equal(Some "aegis:finding/SF-0001", item.Sources.Head.ExternalId)
    Assert.Empty intake.Warnings

[<Fact>]
let ``unknown fields of the received provenance are preserved`` () =
    let upstream = receivedBlock (aegisIntake ()).Item
    Assert.Contains(upstream.Extensions, fun (name, _) -> name = "subject")
    Assert.Contains((snd upstream.Contributions.Head).Extensions, fun (name, value) -> name = "x-severity" && value.Text = "\"high\"")

[<Fact>]
let ``a requesting agent is the creator, distinct from the generating system and the discoverer`` () =
    let item =
        receive
            defaultOptions
            noneStored
            (envelope "op-claude-0001" claude (Some "EXE-20260926T090500000Z-d1d1d1d1") (Some findingProvenance))
            payload
        |> received
        |> fun intake -> intake.Item

    let own = ownBlock item

    Assert.Equal(Some "EXE-20260926T090500000Z-d1d1d1d1", Provenance.originator own |> Option.map fst)
    Assert.Equal(Some claude, Provenance.originator own |> Option.map (fun (_, e) -> e.Actor))
    Assert.Equal({ Type = ActorType.Agent; Name = "anthropic/claude-code" }, item.CreatedBy)
    Assert.Equal(gemini, (snd (receivedBlock item).Contributions.Head).Actor)

[<Fact>]
let ``handled by another agent, then resolved and reviewed by a human: every role keeps its own key`` () =
    let item = (aegisIntake ()).Item

    let handled =
        item
        |> Attribution.addNote (attribution codex (Some "EXE-20260926T100000000Z-b1b1b1b1") "op-note-1" "2026-09-26T10:00:00.000Z") "Patched the query builder."
        |> ok
        |> Attribution.contribute [ Operation.Remediated ] (attribution codex (Some "EXE-20260926T100000000Z-b1b1b1b1") "op-fix-1" "2026-09-26T10:01:00.000Z")
        |> ok

    let kevin = ProvenanceActor.human "kevin"

    let resolved =
        handled
        |> Attribution.resolve (attribution kevin (Some "CTB-20260926-5f2e19aa") "op-resolve-1" "2026-09-26T11:00:00.000Z") Completed None (Some "Fixed and verified.")
        |> ok

    let own = ownBlock resolved

    Assert.Equal<string list>(
        [ "EXT-aegis.review-20260926-01"
          "EXT-vigila.op-aegis-0001"
          "EXE-20260926T100000000Z-b1b1b1b1"
          "CTB-20260926-5f2e19aa" ],
        own.Contributions |> List.map fst
    )

    Assert.Equal<string list>([ "EXE-20260926T100000000Z-b1b1b1b1" ], keysWith Operation.Remediated own)
    Assert.Equal<string list>([ "EXE-20260926T100000000Z-b1b1b1b1" ], keysWith Operation.Modified own)
    Assert.Equal<string list>([ "CTB-20260926-5f2e19aa" ], keysWith Operation.Resolved own)
    // The original creator was never replaced.
    Assert.Equal(Some "EXT-aegis.review-20260926-01", Provenance.originator own |> Option.map fst)
    Assert.Equal(item.CreatedBy, resolved.CreatedBy)

    // History and notes point at their contribution.
    Assert.Equal(Some "EXE-20260926T100000000Z-b1b1b1b1", resolved.Notes.Head.Contribution)
    Assert.Equal({ Type = ActorType.Agent; Name = "openai/codex" }, resolved.Notes.Head.CreatedBy)
    let last = List.last resolved.History
    Assert.Equal(Some "CTB-20260926-5f2e19aa", last.Contribution)
    Assert.Equal({ Type = Human; Name = "kevin" }, last.Actor)
    Assert.Equal(Completed, resolved.Status)
    Assert.Equal(Some "Fixed and verified.", resolved.ResolutionNote)

    let reviewed =
        { resolved with NeedsReview = true }
        |> Attribution.review (attribution kevin (Some "CTB-20260926-6a7b8c9d") "op-review-1" "2026-09-26T11:30:00.000Z")
        |> ok

    Assert.Equal<string list>([ "CTB-20260926-6a7b8c9d" ], keysWith Operation.Reviewed (ownBlock reviewed))
    Assert.False reviewed.NeedsReview

[<Fact>]
let ``two executions of the same agent stay two contributions`` () =
    let item =
        (aegisIntake ()).Item
        |> Attribution.addNote (attribution codex (Some "EXE-20260926T100000000Z-a1a1a1a1") "op-1" "2026-09-26T10:00:00.000Z") "First pass."
        |> ok
        |> Attribution.resolve (attribution codex (Some "EXE-20260926T120000000Z-a2a2a2a2") "op-2" "2026-09-26T12:00:00.000Z") Completed None None
        |> ok

    let byCodex =
        (ownBlock item).Contributions |> List.filter (fun (_, e) -> e.Actor = codex) |> List.map fst

    Assert.Equal<string list>([ "EXE-20260926T100000000Z-a1a1a1a1"; "EXE-20260926T120000000Z-a2a2a2a2" ], byCodex)

[<Fact>]
let ``an unknown actor with no execution is recorded as unknown under the operation key`` () =
    let item =
        receive defaultOptions noneStored (envelope "op unknown/1" ProvenanceActor.unknown None None) payload
        |> received
        |> fun intake -> intake.Item

    let own = ownBlock item
    Assert.Equal(Some "EXT-op.op_20unknown_2f1", Provenance.originator own |> Option.map fst)
    Assert.Equal(Some ProvenanceActor.unknown, Provenance.originator own |> Option.map (fun (_, e) -> e.Actor))
    Assert.Equal({ Type = ActorType.Unknown; Name = "unknown" }, item.CreatedBy)
    Assert.Equal(None, item.ReceivedProvenance)
    Assert.Equal(Some [ "aegis:finding/SF-0001" ], own.DerivedFrom)

[<Fact>]
let ``a v1 envelope is mapped by the Praxis rules without invention`` () =
    let v1 =
        """{"schema":"echelon.execution-envelope/v1","operationId":"op-v1","correlationId":"c-1",
            "timestamp":"2026-09-26T09:05:00Z",
            "actor":{"kind":"system","provider":{"state":"unknown"},"identity":{"state":"known","value":"aegis-bot"},
                     "runId":{"state":"known","value":"run 7"}}}"""

    let item = (receive defaultOptions noneStored v1 payload |> received).Item

    match Provenance.originator (ownBlock item) with
    | Some(key, entry) ->
        Assert.Equal("EXT-run.run_207", key)
        Assert.Equal(ProvenanceActor.automation "aegis-bot" "unknown" "unknown", entry.Actor)
    | None -> failwith "no originator"

    let human =
        """{"schema":"echelon.execution-envelope/v1","operationId":"op-v1-h","correlationId":"c-2",
            "timestamp":"2026-09-26T09:05:00Z",
            "actor":{"kind":"human","provider":{"state":"not-applicable"},"identity":{"state":"known","value":"kevin"}}}"""

    match readEnvelope human with
    | Ok parsed ->
        Assert.Equal(ProvenanceActor.human "kevin", parsed.Actor)
        Assert.Equal("EXT-op.op-v1-h", parsed.ContributionKey)
    | Error e -> failwith $"%A{e}"

[<Fact>]
let ``malformed received provenance rejects the request with the problems named`` () =
    let malformed = """{"schema":"praxis.provenance/1","contributions":{"EXE-1":{"operations":["created","created"],"at":"2026-09-26T08:00:00.000Z","actor":{"kind":"human","id":"kevin"}}}}"""

    match receive defaultOptions noneStored (envelope "op-bad" aegis None (Some malformed)) payload with
    | Error e ->
        Assert.Equal("ValidationFailed", e.Code)
        Assert.Contains(e.Problems, fun p -> p.StartsWith "envelope.provenance")
    | Ok _ -> failwith "malformed provenance must be rejected, not dropped"

[<Fact>]
let ``a credential in the envelope actor is rejected`` () =
    let leaky = ProvenanceActor.human "ghp_0123456789abcdefghijABCDEFGHIJ0123"

    match receive defaultOptions noneStored (envelope "op-leak" leaky None None) payload with
    | Error e -> Assert.Equal("ValidationFailed", e.Code)
    | Ok _ -> failwith "a credential-like actor must be rejected"

[<Fact>]
let ``an unsupported major is carried verbatim and never read for lineage or merged`` () =
    let future = """{"schema":"praxis.provenance/2","contributions":[{"totally":"different"}],"derivedFrom":["hidden:ref/1"]}"""
    let item = (receive defaultOptions noneStored (envelope "op-future" aegis None (Some future)) payload |> received).Item

    match item.ReceivedProvenance with
    | Some(CarriedVerbatim(schema, json)) ->
        Assert.Equal("praxis.provenance/2", schema)
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse future, System.Text.Json.Nodes.JsonNode.Parse json.Text))
    | other -> failwith $"expected verbatim, got %A{other}"

    Assert.Equal(Some [ "aegis:finding/SF-0001" ], (ownBlock item).DerivedFrom)

[<Fact>]
let ``a forward-compatible operation in received provenance is tolerated and reported`` () =
    let newer = """{"schema":"praxis.provenance/1","contributions":{"EXE-20260926T080000000Z-aaaa0001":{"operations":["created","quarantined"],"at":"2026-09-26T08:00:00.000Z","actor":{"kind":"agent","id":"openai/codex","provider":"openai","model":"gpt-5-codex","runtime":"codex"}}}}"""
    let intake = receive defaultOptions noneStored (envelope "op-newer" aegis None (Some newer)) payload |> received
    Assert.Equal(1, intake.Warnings.Length)

[<Fact>]
let ``replaying an operation returns the stored item without duplicating a contribution`` () =
    let first = aegisIntake ()
    let request = envelope "op-aegis-0001" aegis (Some "EXT-aegis.review-20260926-01") (Some findingProvenance)

    // Pure: the same request builds the same item.
    let again = receive defaultOptions noneStored request payload |> received
    Assert.Equal(first.Item, again.Item)

    // Replayed against the stored item -- even after it was handled.
    let handled =
        first.Item
        |> Attribution.resolve (attribution codex (Some "EXE-20260926T100000000Z-a2a2a2a2") "op-r" "2026-09-26T10:05:00.000Z") Completed None None
        |> ok

    let replay =
        receive defaultOptions (fun id -> if id = handled.Id then Some handled else None) request payload
        |> received

    Assert.True replay.Replayed
    Assert.Equal(handled, replay.Item)
    Assert.Equal(3, (ownBlock replay.Item).Contributions.Length)

[<Fact>]
let ``reusing an operation id for a different request is refused`` () =
    let first = aegisIntake ()
    let other = envelope "op-aegis-0001" claude (Some "EXE-20260926T090500000Z-d1d1d1d1") None

    match receive defaultOptions (fun _ -> Some first.Item) other payload with
    | Error e -> Assert.Equal("OperationAlreadyProcessed", e.Code)
    | Ok _ -> failwith "a different request under the same operation id must be refused"

[<Fact>]
let ``the payload is validated against followup.create v1`` () =
    let bad = """{"title":"x","reason":"y","requestedAction":"nag","priority":"high","colour":"red"}"""

    match receive defaultOptions noneStored (envelope "op-v" aegis None None) bad with
    | Error e ->
        Assert.Equal("ValidationFailed", e.Code)
        Assert.Contains(e.Problems, fun p -> p.Contains "requestedAction")
        Assert.Contains(e.Problems, fun p -> p.Contains "colour")
    | Ok _ -> failwith "an invalid payload must be refused"

[<Fact>]
let ``a legacy item gains provenance only from a real contribution and never an invented creator`` () =
    let author =
        match Actor.human "Kevin" with
        | Ok a -> a
        | Error e -> failwith e

    let title =
        match Title.create "Legacy item" with
        | Ok t -> t
        | Error e -> failwith e

    let legacy = Item.create (Clock.fixedAt (instant "2026-09-01T09:00:00Z")) author CreatedVia.UI title
    Assert.Equal(None, legacy.Provenance)

    let resolved =
        legacy
        |> Attribution.resolve (attribution codex (Some "EXE-20260926T100000000Z-a2a2a2a2") "op-l" "2026-09-26T10:00:00.000Z") Completed None None
        |> ok

    let block = ownBlock resolved
    Assert.Equal(None, Provenance.originator block)
    Assert.Equal<string list>([ "EXE-20260926T100000000Z-a2a2a2a2" ], block.Contributions |> List.map fst)
    Assert.Equal(author, resolved.CreatedBy)

[<Fact>]
let ``an item whose own provenance is another major is never appended to`` () =
    let item =
        { (aegisIntake ()).Item with
            Provenance = Some(CarriedVerbatim("praxis.provenance/2", RawJson """{"schema":"praxis.provenance/2"}""")) }

    match Attribution.resolve (attribution codex None "op-x" "2026-09-26T10:00:00.000Z") Completed None None item with
    | Error(ProvenanceRefused _) -> ()
    | other -> failwith $"expected a refusal, got %A{other}"

[<Fact>]
let ``an illegal transition is refused before any contribution is recorded`` () =
    match Attribution.changeStatus (attribution codex None "op-y" "2026-09-26T10:00:00.000Z") Open None None (aegisIntake ()).Item with
    | Error(TransitionRefused _) -> ()
    | other -> failwith $"expected a transition refusal, got %A{other}"

// --- contract revision 1.1 and echelon-registry REG-PROV-008 ---------------

let private v1With (repository: string option option) (runId: string option option) (operationId: string) =
    let known value = $"""{{"state":"known","value":"%s{value}"}}"""

    let runPart =
        match runId with
        | None -> ""
        | Some None -> ""","runId":{"state":"unknown"}"""
        | Some(Some run) -> $""","runId":%s{known run}"""

    let sourcePart =
        match repository with
        | None -> ""
        | Some None -> ""","source":{"repository":{"state":"unknown"}}"""
        | Some(Some repo) -> $""","source":{{"repository":%s{known repo}}}"""

    $"""{{"schema":"echelon.execution-envelope/v1","operationId":"%s{operationId}","correlationId":"c",
         "timestamp":"2026-09-26T09:00:00Z",
         "actor":{{"kind":"agent","provider":%s{known "openai"},"identity":%s{known "openai/codex"}%s{runPart}}}%s{sourcePart}}}"""

let private v1Key repository runId operationId =
    match readEnvelope (v1With repository runId operationId) with
    | Ok parsed -> parsed.ContributionKey
    | Error e -> failwith $"%A{e}"

[<Fact>]
let ``v1 runs are namespaced by a known source repository, exactly as the registry keys them`` () =
    Assert.Equal("EXT-run.kemiller2002_2faegis.7", v1Key (Some(Some "kemiller2002/aegis")) (Some(Some "7")) "op-1")
    Assert.Equal("EXT-run.kemiller2002_2fvigila.7", v1Key (Some(Some "kemiller2002/vigila")) (Some(Some "7")) "op-1")
    Assert.Equal("EXT-run.octo_2frepo_2ejs.gh_2f99", v1Key (Some(Some "octo/repo.js")) (Some(Some "gh/99")) "op-1")

[<Fact>]
let ``v1 runs without a known repository keep the Praxis key, and unknown runs stay EXT-op`` () =
    Assert.Equal("EXT-run.gh_2f99", v1Key None (Some(Some "gh/99")) "op-1")
    Assert.Equal("EXT-run.gh_2f99", v1Key (Some None) (Some(Some "gh/99")) "op-1")
    Assert.Equal("EXT-op.op_201", v1Key (Some(Some "kemiller2002/aegis")) (Some None) "op 1")
    Assert.Equal("EXT-op.op-1", v1Key (Some(Some "kemiller2002/aegis")) None "op-1")

[<Fact>]
let ``namespaced v1 run keys are injective across repositories containing dots`` () =
    let keys =
        [ "a/b.c", "5"; "a/b", "c.5"; "a.b/c", "5"; "a/b_2ec", "5" ]
        |> List.map (fun (repo, run) -> v1Key (Some(Some repo)) (Some(Some run)) "op-1")

    Assert.Equal(keys.Length, (List.distinct keys).Length)

    for key in keys do
        Assert.Equal(ForeignExecution, Provenance.keyKind key)

let private rejected (json: string) =
    match readEnvelope json with
    | Error e -> e.Problems
    | Ok _ -> failwith "the envelope should have been rejected"

[<Fact>]
let ``an unknown non-extension envelope property is rejected, as the registry schema requires`` () =
    let unknown = (envelope "op-u" aegis None None).Replace("\"x-trace\":\"t-1\"", "\"trace\":\"t-1\"")
    Assert.Contains(rejected unknown, fun p -> p.Contains "envelope.trace")
    // x-... extensions stay allowed on v2.
    Assert.True(Result.isOk (readEnvelope (envelope "op-x" aegis None None)))

[<Fact>]
let ``a v1 envelope is held to the v1 schema`` () =
    let base' = v1With None (Some(Some "7")) "op-1"
    Assert.True(Result.isOk (readEnvelope base'))
    Assert.Contains(rejected (base'.Replace("\"correlationId\":\"c\"", "\"correlationId\":\"c\",\"x-trace\":1")), fun p -> p.Contains "x-trace")
    Assert.Contains(rejected (base'.Replace("{\"state\":\"known\",\"value\":\"openai\"}", "{\"state\":\"maybe\"}")), fun p -> p.Contains "state")
    Assert.Contains(rejected (base'.Replace(",\"identity\":{\"state\":\"known\",\"value\":\"openai/codex\"}", "")), fun p -> p.Contains "identity")

[<Fact>]
let ``null never means absent in an envelope`` () =
    let withNull = (envelope "op-n" aegis None None).Replace("\"x-trace\":\"t-1\"", "\"provenance\":null")
    Assert.Contains(rejected withNull, fun p -> p.StartsWith "envelope.provenance")
    let nullExecution = (envelope "op-n" aegis None None).Replace("\"x-trace\":\"t-1\"", "\"execution\":null")
    Assert.Contains(rejected nullExecution, fun p -> p.Contains "execution")

[<Fact>]
let ``an execution or timestamp that is not exact is rejected`` () =
    Assert.Contains(rejected (envelope "op-e" aegis (Some "EXE-20260926T090500000Z-d1d1d1d1\\n") None), fun p -> p.Contains "execution")
    Assert.Contains(rejected ((envelope "op-t" aegis None None).Replace("2026-09-26T09:05:00.000Z", "2026-09-26")), fun p -> p.Contains "timestamp")

[<Fact>]
let ``an operation id is escaped injectively in every derived key`` () =
    let item = (receive defaultOptions noneStored (envelope "op_1" aegis None None) payload |> received).Item
    let keys = (ownBlock item).Contributions |> List.map fst
    Assert.Equal<string list>([ "EXT-op.op_5f1"; "EXT-vigila.op_5f1" ], keys)
