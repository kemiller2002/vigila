/// Persisting provenance: schema version 2, legacy version 1 records, blocks
/// carried verbatim, malformed blocks rejected (VIG-PROV-006, VIG-PROV-007,
/// VIG-PROV-008, VIG-PROV-017).
module Vigila.Host.GitHub.ProvenancePersistenceTests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Xunit
open Vigila.Semantic.Time
open Vigila.Semantic.Actors
open Vigila.Semantic.Items
open Vigila.Semantic.Item
open Vigila.Semantic.Provenance
open Vigila.Application
open Vigila.Application.Attribution
open Vigila.Application.FollowUpIntake
open Vigila.Host.GitHub.ItemJson

let private load json =
    match fromJson json with
    | Ok item -> item
    | Error e -> failwith $"load failed: %s{e}"

let private roundTrip item = item |> toJson |> load

let private version (json: string) =
    use doc = JsonDocument.Parse json
    doc.RootElement.GetProperty("schemaVersion").GetInt32()

let private instant text =
    match Instant.parse text with
    | Ok value -> value
    | Error e -> failwith e

let private ok result =
    match result with
    | Ok value -> value
    | Error(e: AttributionError) -> failwith $"%A{e}"

let private aegis = ProvenanceActor.automation "echelon/aegis" "echelon" "aegis"
let private codex = ProvenanceActor.agent "openai/codex" "openai" "gpt-5-codex" "codex"

let private envelope operationId (actor: ProvenanceActor) (execution: string) (provenance: string option) =
    let provenancePart =
        match provenance with
        | Some block -> $""","provenance":%s{block}"""
        | None -> ""

    $"""{{"schema":"echelon.execution-envelope/v2","operationId":"%s{operationId}","correlationId":"c",
         "timestamp":"2026-09-26T09:05:00.000Z","actor":%s{(ProvenanceJson.actorToNode actor).ToJsonString()},
         "execution":"%s{execution}"%s{provenancePart}}}"""

let private payload =
    """{"title":"Follow up on SF-0001","reason":"Security finding.","requestedAction":"review","priority":"normal",
        "context":{"source":{"ref":"aegis:finding/SF-0001"}}}"""

let private intake operationId actor execution provenance =
    match receive defaultOptions (fun _ -> None) (envelope operationId actor execution provenance) payload with
    | Ok result -> result.Item
    | Error e -> failwith $"%A{e}"

/// A record exactly as a build before this change wrote it: schema version 1,
/// `{type, name}` actors, no provenance anywhere.
let legacyRecord =
    """{
  "schemaVersion": 1,
  "id": "3f2a1c4e-5b6d-4e7f-8a9b-0c1d2e3f4a5b",
  "title": "Call accountant about generator",
  "kind": "follow-up",
  "status": "open",
  "description": null,
  "nextAction": null,
  "due": null,
  "followUp": null,
  "snoozedUntil": null,
  "waitingOn": null,
  "waitingSince": null,
  "tags": [
    "tax"
  ],
  "notes": [
    {
      "id": "9a8b7c6d-5e4f-4a3b-9c2d-1e0f9a8b7c6d",
      "text": "Asked for the cancelled check.",
      "createdAt": "2026-09-18T09:30:00.0000000-04:00",
      "createdBy": {
        "type": "agent",
        "name": "Claude"
      }
    }
  ],
  "sources": [],
  "important": false,
  "needsReview": false,
  "resolution": null,
  "resolutionNote": null,
  "createdAt": "2026-09-18T09:30:00.0000000-04:00",
  "createdBy": {
    "type": "human",
    "name": "Kevin"
  },
  "createdVia": "ui",
  "updatedAt": "2026-09-18T09:30:00.0000000-04:00",
  "completedAt": null,
  "cancelledAt": null,
  "lastActivityAt": "2026-09-18T09:30:00.0000000-04:00",
  "history": [
    {
      "at": "2026-09-18T09:30:00.0000000-04:00",
      "actor": {
        "type": "human",
        "name": "Kevin"
      },
      "operation": "created"
    }
  ]
}"""

[<Fact>]
let ``a legacy version 1 record reads as unattributed and writes back unchanged`` () =
    // VIG-PROV-008: nothing is inferred from {type, name}; VIG-PROV-007:
    // an untouched record is written exactly as before.
    let item = load legacyRecord
    Assert.Equal(None, item.Provenance)
    Assert.Equal(None, item.ReceivedProvenance)
    Assert.Equal({ Type = Human; Name = "Kevin" }, item.CreatedBy)
    Assert.Equal({ Type = ActorType.Agent; Name = "Claude" }, item.Notes.Head.CreatedBy)
    Assert.Equal(None, item.Notes.Head.Contribution)
    Assert.Equal(legacyRecord.ReplaceLineEndings "\n", (toJson item).ReplaceLineEndings "\n")

[<Fact>]
let ``a version 1 record may not claim the unknown actor type`` () =
    let claimed = legacyRecord.Replace("\"type\": \"human\",\n    \"name\": \"Kevin\"", "\"type\": \"unknown\",\n    \"name\": \"Kevin\"")
    Assert.NotEqual<string>(legacyRecord, claimed)
    Assert.True(Result.isError (fromJson claimed))

[<Fact>]
let ``an item with provenance is written as version 2 and round-trips exactly`` () =
    let item = intake "op-1" aegis "EXT-aegis.review-1" None
    let json = toJson item
    Assert.Equal(CurrentSchemaVersion, version json)
    Assert.Equal(item, load json)
    // Stable: a second write is byte-identical.
    Assert.Equal(json, toJson (load json))

[<Fact>]
let ``unknown provenance fields survive a save and load`` () =
    let received =
        """{"schema":"praxis.provenance/1","x-block":{"n":1},
            "contributions":{"EXE-20260926T090000000Z-c1c1c1c1":{"operations":["created","discovered","x-triaged"],
              "at":"2026-09-26T09:00:00.000Z","x-entry":[1,2],
              "actor":{"kind":"agent","id":"google/gemini-cli","provider":"google","model":"unknown","runtime":"gemini-cli","x-actor":true}}},
            "derivedFrom":["git:commit/5e1f0c2"]}"""

    let item = intake "op-2" aegis "EXT-aegis.review-2" (Some received)

    let stored =
        match (JsonNode.Parse(toJson item)) with
        | null -> failwith "no json"
        | node -> node["receivedProvenance"]

    Assert.True(JsonNode.DeepEquals(JsonNode.Parse received, stored), "the received block changed on save")
    Assert.Equal(item, roundTrip item)

[<Fact>]
let ``an unsupported major is carried verbatim through save and load`` () =
    let future = """{"schema":"praxis.provenance/2","contributions":[{"totally":"different"}],"whatever":true}"""
    let item = intake "op-3" aegis "EXT-aegis.review-3" (Some future)
    let json = toJson item
    let restored = load json

    match restored.ReceivedProvenance with
    | Some(CarriedVerbatim(schema, text)) ->
        Assert.Equal("praxis.provenance/2", schema)
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse future, JsonNode.Parse text.Text))
    | other -> failwith $"expected verbatim, got %A{other}"

    Assert.Equal(json, toJson restored)

    // An item's own block of another major is carried the same way, and is
    // never appended to.
    let ownFuture =
        { item with Provenance = Some(CarriedVerbatim("praxis.provenance/2", RawJson future)) }

    Assert.Equal(ownFuture, roundTrip ownFuture)

    match Attribution.resolve { Actor = codex; Execution = None; OperationId = "op-x"; At = instant "2026-09-26T10:00:00Z"; Reason = None } Completed None None ownFuture with
    | Error(ProvenanceRefused _) -> ()
    | other -> failwith $"expected a refusal, got %A{other}"

[<Fact>]
let ``a stored malformed block fails the load with the problem named, never dropped`` () =
    let item = intake "op-4" aegis "EXT-aegis.review-4" None
    let json = toJson item
    let broken = json.Replace("\"EXT-aegis.review-4\": {", "\"not-a-key\": {")
    Assert.NotEqual<string>(json, broken)

    match fromJson broken with
    | Error message ->
        Assert.Contains("provenance", message)
        Assert.Contains("not-a-key", message)
    | Ok _ -> failwith "a malformed provenance block must fail the load"

[<Fact>]
let ``an unknown actor is persisted as version 2`` () =
    let item = intake "op-5" ProvenanceActor.unknown "EXT-op.op-5" None
    let json = toJson item
    Assert.Equal(2, version json)
    Assert.Equal({ Type = ActorType.Unknown; Name = "unknown" }, (load json).CreatedBy)

[<Fact>]
let ``the echelon-chain follow-up replayed through Vigila reproduces its originator and roles`` () =
    // echelon-chain.json, record vigila:followup/FU-0001: Vigila generates the
    // follow-up from the Aegis finding, another execution of agent A resolves
    // it, and a human reviews the resolution.
    let chainPath =
        let rec root (d: DirectoryInfo) =
            if File.Exists(Path.Combine(d.FullName, "Vigila.sln")) then d.FullName
            else match d.Parent with null -> failwith "no root" | p -> root p

        Path.Combine(root (DirectoryInfo AppContext.BaseDirectory), "tests", "fixtures", "praxis-provenance", "echelon-chain.json")

    let expected =
        match JsonNode.Parse(File.ReadAllText chainPath) with
        | null -> failwith "empty chain"
        | chain -> chain

    let expectAt (path: string list) =
        path |> List.fold (fun (node: JsonNode | null) name -> match node with null -> null | n -> n[name]) expected

    let strings (node: JsonNode | null) =
        match node with
        | :? JsonArray as items -> items |> Seq.map (fun i -> match i with null -> "" | v -> v.GetValue<string>()) |> Seq.toList
        | _ -> []

    let value (node: JsonNode | null) = match node with null -> "" | v -> v.GetValue<string>()

    let generated =
        match
            receive
                { RecordTransformation = false }
                (fun _ -> None)
                (envelope "op-followup-0001" ProvenanceActor.vigila "EXT-vigila.op-followup-0001" None)
                payload
        with
        | Ok result -> result.Item
        | Error e -> failwith $"%A{e}"

    let kevin = ProvenanceActor.human "kevin"

    let finished =
        generated
        |> Attribution.resolve
            { Actor = codex
              Execution = Some "EXE-20260926T100000000Z-a2a2a2a2"
              OperationId = "op-resolve"
              At = instant "2026-09-26T10:05:00.000Z"
              Reason = Some "The fixing execution resolves the follow-up" }
            Completed
            None
            None
        |> ok
        |> Attribution.review
            { Actor = kevin
              Execution = Some "CTB-20260926-5f2e19aa"
              OperationId = "op-review"
              At = instant "2026-09-26T11:00:00.000Z"
              Reason = Some "Human reviews the resolution" }
        |> ok
        |> roundTrip

    let block =
        match finished.Provenance with
        | Some(Recorded b) -> b
        | other -> failwith $"%A{other}"

    let record = "vigila:followup/FU-0001"

    match Provenance.originator block with
    | Some(key, entry) ->
        Assert.Equal(value (expectAt [ "expect"; "originators"; record; "key" ]), key)
        Assert.Equal(value (expectAt [ "expect"; "originators"; record; "actorId" ]), entry.Actor.Id)
    | None -> failwith "no originator"

    for role in [ "resolved"; "reviewed" ] do
        Assert.Equal<string list>(
            strings (expectAt [ "expect"; "roles"; record; role ]),
            Provenance.withRole (Operation.ofCode role) block |> List.map fst
        )

    Assert.Equal(Some [ "aegis:finding/SF-0001" ], block.DerivedFrom)
    // Three roles, three identities: none collapsed into another.
    Assert.Equal(3, block.Contributions |> List.map (fun (_, e) -> e.Actor) |> List.distinct |> List.length)
