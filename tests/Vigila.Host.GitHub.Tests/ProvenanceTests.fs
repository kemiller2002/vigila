/// Agent identity and provenance: the Praxis conformance fixtures, the
/// end-to-end follow-up chain driven through Vigila's own operations, and the
/// persisted round trips.
///
/// Requirements: VIG-AGT-037, VIG-DOM-050 .. VIG-DOM-055, VIG-PER-024,
/// VIG-PER-025.
module Vigila.Host.GitHub.ProvenanceTests

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open Xunit
open Vigila.Semantic.Time
open Vigila.Semantic.Carried
open Vigila.Semantic.Actors
open Vigila.Semantic.Provenance
open Vigila.Semantic.Items
open Vigila.Semantic.History
open Vigila.Semantic.Item
open Vigila.Host.GitHub.ProvenanceJson
open Vigila.Host.GitHub.ItemJson

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private ok result =
    match result with
    | Ok value -> value
    | Error(e: string) -> failwith e

let private okProblems result =
    match result with
    | Ok value -> value
    | Error(problems: ProvenanceProblem list) ->
        failwith (problems |> List.map (fun p -> $"{p.Field}: {p.Message}") |> String.concat "; ")

let private at text = ok (Instant.parse text)

let private fixtures =
    let rec up (dir: DirectoryInfo | null) =
        match dir with
        | Null -> failwith "could not find the repository root (Vigila.sln)"
        | NonNull d when File.Exists(Path.Combine(d.FullName, "Vigila.sln")) -> d.FullName
        | NonNull d -> up d.Parent

    Path.Combine(up (DirectoryInfo AppContext.BaseDirectory), "tests", "fixtures", "praxis-provenance-record")

let private fixtureText (relative: string) = File.ReadAllText(Path.Combine(fixtures, relative))

let private fixture relative = ok (CarriedJson.parse (fixtureText relative))

let private manifest = lazy (JsonDocument.Parse(fixtureText "manifest.json"))

let private entries (name: string) =
    manifest.Value.RootElement.GetProperty(name).EnumerateArray() |> Seq.toList

let private str (el: JsonElement) (name: string) =
    match el.GetProperty(name).GetString() with
    | NonNull text -> text
    | Null -> failwith $"'{name}' is null"

let private titled text = ok (Title.create text)

let private attribution actor key =
    ok (Attribution.create actor (Some(ok (ContributionKey.parse key))))

// The actors of the end-to-end chain (manifest e2e, steps 05-09).
let private vigila = ok (Actor.identify AutomatedProcess "echelon/vigila" "echelon" "unknown" "vigila")
let private codex = ok (Actor.identify Agent "openai/codex" "openai" "gpt-5-codex" "codex")
let private claude = ok (Actor.identify Agent "anthropic/claude-code" "anthropic" "unknown" "claude-code")
let private kevin = ok (Actor.human "kevin")

let private generatedIn = "EXE-vigila.20260926T130000000Z-0c0c0c0c"
let private handledIn = "EXE-20260926T140000000Z-e5e5e5e5"
let private resolvedIn = "EXE-20260926T150000000Z-f6f6f6f6"
let private validatedIn = "CTB-20260926-5f2e19aa"

let private roundTrip item =
    match toJson item |> fromJson with
    | Ok restored -> restored
    | Error e -> failwith $"round trip failed: %s{e}"

let private assertEquivalent (expected: Verbatim) (actual: Verbatim) =
    if not (Verbatim.equivalent expected actual) then
        failwith $"records differ.\nexpected:\n{CarriedJson.render true expected}\nactual:\n{CarriedJson.render true actual}"

// ---------------------------------------------------------------------------
// Vendored fixtures (VIG-AGT-037)
// ---------------------------------------------------------------------------

[<Fact>]
let ``the vendored Praxis fixtures match the digests they were vendored with`` () =
    use source = JsonDocument.Parse(fixtureText "SOURCE.json")
    let root = source.RootElement
    Assert.Equal("kemiller2002/praxis", str root "repository")
    Assert.Equal("schemas/conformance/provenance-record", str root "path")

    let recorded =
        root.GetProperty("files").EnumerateObject()
        |> Seq.map (fun p -> p.Name, (match p.Value.GetString() with NonNull t -> t | Null -> ""))
        |> Map.ofSeq

    let present =
        Directory.GetFiles(fixtures, "*", SearchOption.AllDirectories)
        |> Array.map (fun path -> Path.GetRelativePath(fixtures, path).Replace('\\', '/'))
        |> Array.filter ((<>) "SOURCE.json")
        |> Set.ofArray

    Assert.Equal<Set<string>>(recorded |> Map.keys |> Set.ofSeq, present)

    for KeyValue(relative, digest) in recorded do
        let actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixtures, relative)))).ToLowerInvariant()
        Assert.True((digest = actual), $"{relative}: recorded {digest}, found {actual}")

[<Fact>]
let ``every conformance case reads as the manifest expects`` () =
    let cases = entries "cases"
    Assert.NotEmpty cases

    for case in cases do
        let file = str case "file"
        let expected = str case "expect"

        let found =
            match ProvenanceRecordJson.parse (fixtureText file) with
            | Ok(_, Reading.Current _) -> "valid"
            | Ok(_, Reading.Unversioned _) -> "valid-unversioned"
            | Ok(_, Reading.Unsupported _) -> "unsupported-version"
            | Error _ -> "invalid"

        Assert.True((expected = found), $"{file}: expected {expected}, found {found}")

[<Fact>]
let ``every successor pair is judged as the manifest expects`` () =
    let pairs = entries "successors"
    Assert.NotEmpty pairs

    for pair in pairs do
        let before = str pair "before"
        let after = str pair "after"
        let expected = str pair "expect"
        let problems = ProvenanceRecord.successorProblems (fixture before) (fixture after)
        let found = if problems.IsEmpty then "preserved" else "destructive"
        Assert.True((expected = found), $"{before} -> {after}: expected {expected}, found {found} %A{problems}")

[<Fact>]
let ``the end-to-end chain validates and preserves history at every hop`` () =
    let steps = manifest.Value.RootElement.GetProperty("e2e").GetProperty("steps").EnumerateArray() |> Seq.toList
    Assert.Equal(10, steps.Length)

    for step in steps do
        let file = str step "file"

        match ProvenanceRecordJson.parse (fixtureText file) with
        | Ok(_, Reading.Current _) -> ()
        | other -> failwith $"{file}: expected a valid current record, got %A{other}"

        match step.GetProperty("successorOf").ValueKind with
        | JsonValueKind.Null -> ()
        | _ ->
            let previous = str step "successorOf"
            Assert.Empty(ProvenanceRecord.successorProblems (fixture previous) (fixture file))

[<Fact>]
let ``the final record's chain is reconstructed exactly, each link under its own subject`` () =
    let expected = manifest.Value.RootElement.GetProperty("e2e").GetProperty("expected")

    let record =
        match ProvenanceRecord.read (fixture (str expected "final")) with
        | Ok(Reading.Current record) -> record
        | other -> failwith $"%A{other}"

    let wanted =
        expected.GetProperty("chain").EnumerateArray()
        |> Seq.map (fun link ->
            str link "subject",
            str link "key",
            str link "actor",
            link.GetProperty("operations").EnumerateArray() |> Seq.map (fun o -> match o.GetString() with NonNull t -> t | Null -> "") |> Seq.toList)
        |> Seq.toList

    let actual =
        ProvenanceRecord.chain record
        |> List.map (fun link ->
            defaultArg link.Subject "",
            link.Contribution.Key,
            link.Contribution.Actor.Id,
            link.Contribution.Operations |> List.map ContributionOperation.code)

    Assert.Equal<(string * string * string * string list) list>(wanted, actual)

    let origin = expected.GetProperty "originatorOfFinal"
    let originator = ProvenanceRecord.originator record |> Option.get
    Assert.Equal(str origin "key", originator.Key)
    Assert.Equal(str origin "actor", originator.Actor.Id)

// ---------------------------------------------------------------------------
// The Vigila follow-up chain, through Vigila's own operations (VIG-DOM-053)
// ---------------------------------------------------------------------------

/// e2e/06 as a persisted Vigila item: generated by Vigila, carrying the
/// record, and read back through the item file format.
let private followUpFrom06 () =
    let clock = Clock.fixedAt (at "2026-09-26T13:00:00.000Z")
    let creator = attribution vigila generatedIn
    let item = Item.create clock vigila CreatedVia.Integration (titled "Rotate the leaked deploy key (aegis:fault/F-17)")

    { item with
        History = [ History.attributed item.CreatedAt creator Created ]
        Provenance = Some(fixture "e2e/06-vigila-followup.json") }
    |> roundTrip

let private step (instant: string) who key operations reason evidence item =
    Item.contribute (Clock.fixedAt (at instant)) (attribution who key) operations reason evidence item
    |> ok
    |> roundTrip

[<Fact>]
let ``handling, resolving and validating a follow-up yields the Praxis end-to-end record`` () =
    let generated = followUpFrom06 ()

    let handled =
        generated
        |> step "2026-09-26T14:00:00.000Z" codex handledIn [ ContributionOperation.Handled ] (Some "Picked up the follow-up") []

    let resolved =
        handled
        |> step
            "2026-09-26T15:00:00.000Z"
            claude
            resolvedIn
            [ ContributionOperation.Resolved ]
            (Some "Fixed in def456")
            [ "git:commit/def456" ]

    let validated =
        resolved
        |> step
            "2026-09-26T16:00:00.000Z"
            kevin
            validatedIn
            [ ContributionOperation.Validated; ContributionOperation.Approved ]
            (Some "Verified the fix")
            []

    // Each hop equals the corresponding Praxis fixture, and is a
    // non-destructive successor of the one before it.
    let records = [ generated; handled; resolved; validated ] |> List.map (fun item -> item.Provenance.Value)

    List.zip records [ "e2e/06-vigila-followup.json"; "e2e/07-followup-handled.json"; "e2e/08-followup-resolved.json"; "e2e/09-followup-validated.json" ]
    |> List.iter (fun (record, file) -> assertEquivalent (fixture file) record)

    records |> List.pairwise |> List.iter (fun (a, b) -> Assert.Empty(ProvenanceRecord.successorProblems a b))

[<Fact>]
let ``discoverer, generator, handler, resolver and validator stay distinct and recoverable`` () =
    let final =
        followUpFrom06 ()
        |> step "2026-09-26T14:00:00.000Z" codex handledIn [ ContributionOperation.Handled ] (Some "Picked up the follow-up") []
        |> step "2026-09-26T15:00:00.000Z" claude resolvedIn [ ContributionOperation.Resolved ] (Some "Fixed in def456") [ "git:commit/def456" ]
        |> step "2026-09-26T16:00:00.000Z" kevin validatedIn [ ContributionOperation.Validated; ContributionOperation.Approved ] (Some "Verified the fix") []

    // Vigila's own history: one record per role, each with its actor and run.
    let roles =
        final.History
        |> List.map (fun e -> e.Actor.Name, e.Actor.Type, e.Execution |> Option.map ContributionKey.value)

    Assert.Equal<(string * ActorType * string option) list>(
        [ "echelon/vigila", AutomatedProcess, Some generatedIn
          "openai/codex", Agent, Some handledIn
          "anthropic/claude-code", Agent, Some resolvedIn
          "kevin", Human, Some validatedIn ],
        roles
    )

    // The creator is the generating system, not the discovering agent.
    Assert.Equal(vigila, final.CreatedBy)

    let record =
        match ProvenanceRecord.read final.Provenance.Value with
        | Ok(Reading.Current record) -> record
        | other -> failwith $"%A{other}"

    let own = record.Contributions |> List.map (fun c -> c.Key, c.Actor.Id)
    Assert.Equal(4, own |> List.map fst |> List.distinct |> List.length)
    Assert.Equal(4, own |> List.map snd |> List.distinct |> List.length)

    // The discovering agent is recoverable, and only from the lineage snapshot.
    Assert.DoesNotContain("google/gemini-cli", own |> List.map snd)

    let discoverer =
        ProvenanceRecord.chain record
        |> List.find (fun link -> link.Subject = Some "aegis:fault/F-17")

    Assert.Equal("google/gemini-cli", discoverer.Contribution.Actor.Id)
    Assert.Equal("EXE-20260926T120000000Z-d4d4d4d4", discoverer.Contribution.Key)
    Assert.Equal(generatedIn, (ProvenanceRecord.chain record |> List.head).Contribution.Key)

[<Fact>]
let ``Vigila derives the follow-up record of e2e/06 from the finding of e2e/05`` () =
    let creator =
        { Attribution = attribution vigila generatedIn
          Operations = [ ContributionOperation.Created ]
          At = at "2026-09-26T13:00:00.000Z"
          Reason = Some "Follow-up generated from aegis:fault/F-17"
          Evidence = [] }

    let derived =
        ProvenanceRecord.derive
            "vigila:item/ITEM-42"
            creator
            [ "aegis:fault/F-17", Some(fixture "e2e/05-aegis-finding.json") ]
        |> okProblems

    assertEquivalent (fixture "e2e/06-vigila-followup.json") derived

[<Fact>]
let ``a generated item names its system as creator and its source only as lineage`` () =
    let clock = Clock.fixedAt (at "2026-09-26T13:00:00.000Z")
    let run = ok (ContributionKey.foreignExecution SystemName "20260926T130000000Z-0c0c0c0c")
    let creator = ok (Attribution.create vigila (Some run))

    let item =
        Item.generate
            clock
            creator
            CreatedVia.Integration
            (titled "Rotate the leaked deploy key")
            (Some "Follow-up generated from aegis:fault/F-17")
            [ "aegis:fault/F-17", Some(fixture "e2e/05-aegis-finding.json") ]
        |> ok
        |> roundTrip

    Assert.Equal(vigila, item.CreatedBy)
    Assert.Equal(Some run, (List.exactlyOne item.History).Execution)

    let record =
        match ProvenanceRecord.read item.Provenance.Value with
        | Ok(Reading.Current record) -> record
        | other -> failwith $"%A{other}"

    Assert.Equal(Some(ProvenanceRecord.subjectOf item.Id), record.Subject)
    Assert.Equal<string list>([ generatedIn ], record.Contributions |> List.map _.Key)
    Assert.Equal<string list>([ "aegis:fault/F-17" ], record.DerivedFrom)
    assertEquivalent (fixture "e2e/05-aegis-finding.json") (Verbatim.field "sources" item.Provenance.Value |> Option.bind (Verbatim.field "aegis:fault/F-17") |> Option.get)

[<Fact>]
let ``a contribution that would re-attribute an execution is refused and changes nothing`` () =
    let generated = followUpFrom06 ()
    // Claude claiming the run in which Codex handled it.
    let handled = generated |> step "2026-09-26T14:00:00.000Z" codex handledIn [ ContributionOperation.Handled ] None []

    match Item.contribute (Clock.fixedAt (at "2026-09-26T15:00:00.000Z")) (attribution claude handledIn) [ ContributionOperation.Resolved ] None [] handled with
    | Error message -> Assert.Contains("re-attribute", message)
    | Ok _ -> failwith "re-attribution should be refused"

[<Fact>]
let ``a contribution cannot claim creation, and needs a run`` () =
    let generated = followUpFrom06 ()
    let clock = Clock.fixedAt (at "2026-09-26T14:00:00.000Z")

    Assert.True(Item.contribute clock (attribution codex handledIn) [ ContributionOperation.Created ] None [] generated |> Result.isError)
    Assert.True(Item.contribute clock (ok (Attribution.create codex None)) [ ContributionOperation.Handled ] None [] generated |> Result.isError)

[<Fact>]
let ``an agent is never keyed outside an execution`` () =
    Assert.True(Attribution.create codex (Some(ok (ContributionKey.parse "CTB-20260926-12345678"))) |> Result.isError)

// ---------------------------------------------------------------------------
// Persisted round trips (VIG-DOM-050, VIG-DOM-051, VIG-PER-024, VIG-PER-025)
// ---------------------------------------------------------------------------

let private clock = Clock.fixedAt (at "2026-09-26T10:00:00.000Z")

[<Fact>]
let ``an agent actor round-trips with provider, model, runtime and its execution`` () =
    let creator = attribution codex handledIn
    let item = Item.create clock codex CreatedVia.Agent (titled "x")
    let original = { item with History = [ History.attributed item.CreatedAt creator Created ] }
    let restored = roundTrip original

    Assert.Equal(original, restored)
    Assert.Equal(Some { Provider = "openai"; Model = "gpt-5-codex"; Runtime = "codex" }, restored.CreatedBy.Tooling)
    Assert.Equal(Some(ContributionKey.Execution handledIn), (List.head restored.History).Execution)

    use doc = JsonDocument.Parse(toJson original)
    let actor = doc.RootElement.GetProperty "createdBy"
    Assert.Equal("agent", actor.GetProperty("kind").GetString())
    Assert.Equal("openai/codex", actor.GetProperty("id").GetString())
    Assert.Equal(handledIn, doc.RootElement.GetProperty("history").[0].GetProperty("execution").GetString())

[<Fact>]
let ``a human actor omits provider, model and runtime`` () =
    let item = Item.create clock kevin CreatedVia.UI (titled "x")
    use doc = JsonDocument.Parse(toJson item)
    let actor = doc.RootElement.GetProperty "createdBy"

    Assert.Equal("human", actor.GetProperty("kind").GetString())

    for field in [ "provider"; "model"; "runtime" ] do
        Assert.False(actor.TryGetProperty(field) |> fst, $"a human actor should not carry '{field}'")

    Assert.Equal(item, roundTrip item)

[<Fact>]
let ``a human actor that claims a provider is refused`` () =
    let json = (toJson (Item.create clock kevin CreatedVia.UI (titled "x"))).Replace("\"kind\": \"human\"", "\"kind\": \"human\", \"provider\": \"openai\"")
    Assert.True(fromJson json |> Result.isError)

[<Fact>]
let ``unknown actor fields survive a read and a rewrite`` () =
    let json =
        (toJson (Item.create clock codex CreatedVia.Agent (titled "x")))
            .Replace("\"runtime\": \"codex\"", "\"runtime\": \"codex\", \"x-team\": \"platform\", \"attestation\": {\"type\": \"x-future\", \"n\": 1.50}")

    let restored = ok (fromJson json)

    Assert.Equal<(string * Verbatim) list>(
        [ "x-team", Verbatim.String "platform"
          "attestation", Verbatim.Object [ "type", Verbatim.String "x-future"; "n", Verbatim.Number "1.50" ] ],
        restored.CreatedBy.Extensions
    )

    // Rewritten after a further change, the fields are still there, verbatim.
    let rewritten = toJson (Item.setImportant clock codex true restored)
    use doc = JsonDocument.Parse rewritten
    let actor = doc.RootElement.GetProperty "createdBy"
    Assert.Equal("platform", actor.GetProperty("x-team").GetString())
    Assert.Equal("1.50", actor.GetProperty("attestation").GetProperty("n").GetRawText())

[<Fact>]
let ``an actor kind that contradicts its type is refused, not reconciled`` () =
    let json = (toJson (Item.create clock codex CreatedVia.Agent (titled "x"))).Replace("\"kind\": \"agent\"", "\"kind\": \"automation\"")
    Assert.True(fromJson json |> Result.isError)

[<Fact>]
let ``an integration stays an integration through the item file and the provenance record`` () =
    let integration = ok (Actor.identify Integration "acme/importer" "acme" "unknown" "importer")
    let item = Item.create clock integration CreatedVia.Integration (titled "x")

    Assert.Equal(integration, (roundTrip item).CreatedBy)

    let praxis = PraxisActor.valueOf integration
    Assert.Equal(Some "automation", Verbatim.stringField "kind" praxis)
    Assert.Equal(Some "integration", Verbatim.stringField PraxisActor.VigilaTypeField praxis)
    Assert.Equal(Some Integration, PraxisActor.typeOf "automation" (Verbatim.stringField PraxisActor.VigilaTypeField praxis))
    Assert.Equal(Some AutomatedProcess, PraxisActor.typeOf "automation" None)

[<Fact>]
let ``a version 1 item loads without any invented identity or history`` () =
    let legacy =
        """{
  "schemaVersion": 1,
  "id": "3f2a1c4e-1111-2222-3333-444455556666",
  "title": "Legacy item",
  "kind": "follow-up",
  "status": "open",
  "tags": [],
  "notes": [],
  "sources": [],
  "createdAt": "2026-09-18T09:30:00.0000000+00:00",
  "createdBy": { "type": "agent", "name": "Claude" },
  "createdVia": "agent",
  "updatedAt": "2026-09-18T09:30:00.0000000+00:00",
  "lastActivityAt": "2026-09-18T09:30:00.0000000+00:00",
  "history": [
    { "at": "2026-09-18T09:30:00.0000000+00:00", "actor": { "type": "agent", "name": "Claude" }, "operation": "created" },
    { "at": "2026-09-18T10:00:00.0000000+00:00", "actor": { "type": "human", "name": "Kevin" }, "operation": "note-added" }
  ]
}"""

    let item = ok (fromJson legacy)

    Assert.True(item.Provenance.IsNone)
    Assert.All(item.History, fun e -> Assert.True(e.Execution.IsNone))
    Assert.Equal(Some Tooling.unknown, item.CreatedBy.Tooling)
    Assert.Equal(None, (List.last item.History).Actor.Tooling)
    Assert.Empty item.CreatedBy.Extensions

    // Rewritten, it becomes version 2 and still says nothing it did not know.
    let rewritten = toJson item
    use doc = JsonDocument.Parse rewritten
    Assert.Equal(CurrentSchemaVersion, doc.RootElement.GetProperty("schemaVersion").GetInt32())
    Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("provenance").ValueKind)

    for entry in doc.RootElement.GetProperty("history").EnumerateArray() do
        Assert.False(entry.TryGetProperty("execution") |> fst)

    Assert.Equal("unknown", doc.RootElement.GetProperty("createdBy").GetProperty("model").GetString())
    Assert.Equal(item, roundTrip item)

[<Fact>]
let ``a record in an unsupported major version is carried verbatim, never extended`` () =
    let future = fixture "unsupported/future-major.json"
    let item = { Item.create clock vigila CreatedVia.Integration (titled "x") with Provenance = Some future }
    let restored = roundTrip item
    assertEquivalent future restored.Provenance.Value

    let handled = step "2026-09-26T14:00:00.000Z" codex handledIn [ ContributionOperation.Handled ] None [] restored

    // Vigila's own history records the contribution; the foreign record is
    // untouched.
    assertEquivalent future handled.Provenance.Value
    Assert.Equal(Some(ContributionKey.Execution handledIn), (List.last handled.History).Execution)
    Assert.Empty(ProvenanceRecord.successorProblems future handled.Provenance.Value)

[<Theory>]
[<InlineData("invalid/two-created.json")>]
[<InlineData("invalid/agent-missing-model.json")>]
[<InlineData("invalid/credential-in-reason.json")>]
[<InlineData("invalid/malformed-source-snapshot.json")>]
[<InlineData("invalid/wrong-contract.json")>]
let ``an item carrying malformed provenance is refused, not stripped`` (file: string) =
    let good = toJson { Item.create clock vigila CreatedVia.Integration (titled "x") with Provenance = Some(fixture "valid/minimal-agent.json") }
    use doc = JsonDocument.Parse good
    let minimal = doc.RootElement.GetProperty("provenance").GetRawText()
    let bad = good.Replace(minimal, fixtureText file)

    match fromJson bad with
    | Error message -> Assert.Contains("provenance", message)
    | Ok _ -> failwith $"{file} should make the item unreadable"

[<Fact>]
let ``unknown fields in a provenance record survive the item file and a contribution`` () =
    let original = fixture "valid/unknown-fields-preserved.json"
    let item = { Item.create clock vigila CreatedVia.Integration (titled "x") with Provenance = Some original }
    let handled = roundTrip item |> step "2026-09-26T14:00:00.000Z" codex handledIn [ ContributionOperation.Handled ] None []

    Assert.Empty(ProvenanceRecord.successorProblems original handled.Provenance.Value)
    let record = handled.Provenance.Value
    Assert.Equal(Some "praxis", Verbatim.stringField "x-origin-system" record)

[<Fact>]
let ``a legacy unversioned block gains the envelope only when it is extended`` () =
    let legacy = fixture "unversioned/legacy-registry-block.json"
    let item = roundTrip { Item.create clock vigila CreatedVia.Integration (titled "x") with Provenance = Some legacy }
    assertEquivalent legacy item.Provenance.Value

    let handled = step "2026-09-26T14:00:00.000Z" codex handledIn [ ContributionOperation.Handled ] None [] item
    Assert.Equal(Some ProvenanceRecord.ContractName, Verbatim.stringField "contract" handled.Provenance.Value)
    Assert.Empty(ProvenanceRecord.successorProblems legacy handled.Provenance.Value)

[<Fact>]
let ``the same run contributing twice extends its own entry`` () =
    let generated = followUpFrom06 ()

    let twice =
        generated
        |> step "2026-09-26T14:00:00.000Z" codex handledIn [ ContributionOperation.Handled ] (Some "Picked up the follow-up") []
        |> step "2026-09-26T14:30:00.000Z" codex handledIn [ ContributionOperation.Resolved ] None [ "git:commit/def456" ]

    let entry = Verbatim.field "contributions" twice.Provenance.Value |> Option.bind (Verbatim.field handledIn) |> Option.get
    Assert.Equal(Some "2026-09-26T14:30:00.000Z", Verbatim.stringField "last" entry)
    Assert.Equal(Some "2026-09-26T14:00:00.000Z", Verbatim.stringField "at" entry)
    Assert.Equal(Some(Verbatim.strings [ "x-handled"; "x-resolved" ]), Verbatim.field "operations" entry)
    // Both runs of history remain, each its own record.
    Assert.Equal(3, twice.History.Length)

[<Fact>]
let ``a contribution whose reason carries a credential is refused`` () =
    let item = Item.create clock kevin CreatedVia.UI (titled "x")

    let refused =
        Item.contribute clock (attribution codex handledIn) [ ContributionOperation.Handled ] (Some "ran with token ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ012345") [] item

    Assert.True(Result.isError refused)
