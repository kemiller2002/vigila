/// Conformance against the shared Praxis fixtures (VIG-PROV-017).
///
/// The fixtures are vendored unchanged from
/// kemiller2002/praxis@c2657efb4d54f11d0fd0617cc1bcd5b8418601d5 (contract 1.1) into
/// tests/fixtures/praxis-provenance/ with their SHA-256 in SOURCE.json. The
/// codec must reach the reference library's verdict and warning count on every
/// case, and the end-to-end chain must replay to the same originators, roles
/// and lineage.
module Vigila.Application.ProvenanceConformanceTests

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json.Nodes
open Xunit
open Vigila.Semantic.Provenance
open Vigila.Application

let rec private repositoryRoot (directory: DirectoryInfo) =
    if File.Exists(Path.Combine(directory.FullName, "Vigila.sln")) then
        directory.FullName
    else
        match directory.Parent with
        | null -> failwith "Could not locate the repository root (Vigila.sln)."
        | parent -> repositoryRoot parent

let fixtureDirectory =
    Path.Combine(repositoryRoot (DirectoryInfo(AppContext.BaseDirectory)), "tests", "fixtures", "praxis-provenance")

let fixtureText name = File.ReadAllText(Path.Combine(fixtureDirectory, name))

let private fixture name =
    match JsonNode.Parse(fixtureText name) with
    | null -> failwith $"%s{name} is empty"
    | node -> node

/// A required property; fails the test when it is missing.
let private get (node: JsonNode | null) (name: string) : JsonNode =
    match node with
    | null -> failwith $"missing parent of %s{name}"
    | parent ->
        match parent[name] with
        | null -> failwith $"missing %s{name}"
        | value -> value

let private str (node: JsonNode | null) =
    match node with
    | null -> failwith "expected a string"
    | value -> value.GetValue<string>()

let private cases () =
    match (fixture "cases.json")["cases"] with
    | :? JsonArray as items -> items |> Seq.map (fun item -> Option.get (Option.ofObj item)) |> Seq.toList
    | _ -> failwith "cases.json has no cases array"

let private verdictCode verdict =
    match verdict with
    | ProvenanceJson.Supported(_, warnings) -> "supported", warnings.Length
    | ProvenanceJson.Unsupported _ -> "unsupported", 0
    | ProvenanceJson.Malformed _ -> "malformed", 0

[<Fact>]
let ``vendored fixtures are byte-identical to the recorded Praxis source`` () =
    let source = fixture "SOURCE.json"
    Assert.Equal("kemiller2002/praxis", str source["repository"])
    Assert.Equal("c2657efb4d54f11d0fd0617cc1bcd5b8418601d5", str source["commit"])

    let files =
        match source["files"] with
        | :? JsonObject as o -> o |> Seq.map (fun pair -> pair.Key, str pair.Value) |> Seq.toList
        | _ -> failwith "SOURCE.json has no files map"

    Assert.Equal(3, files.Length)

    for name, expected in files do
        let actual =
            SHA256.HashData(File.ReadAllBytes(Path.Combine(fixtureDirectory, name)))
            |> Convert.ToHexString
            |> fun hex -> hex.ToLowerInvariant()

        Assert.True((expected = actual), $"%s{name} was edited locally: expected %s{expected}, got %s{actual}")

[<Fact>]
let ``every conformance case reaches the reference verdict and warning count`` () =
    let all = cases ()
    Assert.True(all.Length >= 40, "expected the full conformance set")

    let mismatches =
        all
        |> List.choose (fun case ->
            let name = str case["name"]
            let expected = str case["expect"], case["warnings"] |> Option.ofObj |> Option.map (fun n -> n.GetValue<int>()) |> Option.defaultValue 0
            let verdict = ProvenanceJson.classify case["block"]
            let actual = verdictCode verdict

            if actual = expected then None else Some $"%s{name}: expected %A{expected}, got %A{actual} (%A{verdict})")

    Assert.True(mismatches.IsEmpty, String.concat "\n" mismatches)

[<Fact>]
let ``a supported block written back is the block received, unknown fields included`` () =
    // VIG-PROV-006: nothing this version does not model is lost.
    for case in cases () do
        match ProvenanceJson.classify case["block"] with
        | ProvenanceJson.Supported(block, _) ->
            let written = ProvenanceJson.toNode block
            Assert.True(JsonNode.DeepEquals(case["block"], written), $"""%s{str case["name"]} changed on write""")

            match ProvenanceJson.classifyText (written.ToJsonString()) with
            | ProvenanceJson.Supported(again, _) -> Assert.Equal(block, again)
            | other -> failwith $"re-reading changed the verdict: %A{other}"
        | _ -> ()

[<Fact>]
let ``an unsupported major is carried as its exact JSON, never interpreted`` () =
    let case = cases () |> List.find (fun c -> str c["name"] = "unsupported-major")

    match ProvenanceJson.classify case["block"] with
    | ProvenanceJson.Unsupported(schema, json) ->
        Assert.Equal("praxis.provenance/2", schema)
        Assert.True(JsonNode.DeepEquals(case["block"], JsonNode.Parse json.Text))
    | other -> failwith $"expected unsupported, got %A{other}"

[<Fact>]
let ``a malformed block names its problems`` () =
    match ProvenanceJson.classifyText """{"schema":"praxis.provenance/1","contributions":{"run-1":{"operations":["created"],"at":"2026-09-26T08:00:00.000Z","actor":{"kind":"human","id":"kevin"}}}}""" with
    | ProvenanceJson.Malformed problems -> Assert.Contains(problems, fun p -> p.Contains "run-1")
    | other -> failwith $"expected malformed, got %A{other}"

// --- the end-to-end chain ---------------------------------------------------

let private parseContribution (node: JsonNode | null) =
    // A contribution is read through the codec by wrapping it in a block.
    let wrapper = JsonObject()
    let contributions = JsonObject()
    contributions["EXE-wrapper"] <- (match node with null -> null | n -> n.DeepClone())
    wrapper["contributions"] <- contributions

    match ProvenanceJson.classify wrapper with
    | ProvenanceJson.Supported(block, _) -> snd block.Contributions.Head
    | other -> failwith $"chain contribution did not parse: %A{other}"

/// Replays every step of echelon-chain.json with Vigila's model and returns
/// the provenance of every record.
let replayChain () =
    let chain = fixture "echelon-chain.json"

    let steps =
        match chain["steps"] with
        | :? JsonArray as items -> items |> Seq.map (fun s -> Option.get (Option.ofObj s)) |> Seq.toList
        | _ -> failwith "no steps"

    steps
    |> List.fold
        (fun (records: Map<string, ProvenanceBlock>) step ->
            let record = str step["record"]
            let current = records |> Map.tryFind record |> Option.defaultValue Provenance.empty

            let next =
                match step["append"], step["lineage"] with
                | (:? JsonObject as append), _ ->
                    match Provenance.append (str append["key"]) (parseContribution append["contribution"]) current with
                    | Ok(block, _) -> block
                    | Error e -> failwith $"%s{record}: %s{e}"
                | _, (:? JsonArray as lineage) -> Provenance.addLineage (lineage |> Seq.map str |> Seq.toList) current
                | _ -> failwith "unknown step"

            Map.add record next records)
        Map.empty

[<Fact>]
let ``the echelon chain replays to the expected originators, roles and lineage`` () =
    let expect = get (fixture "echelon-chain.json") "expect"
    let records = replayChain ()

    match get expect "originators" with
    | :? JsonObject as originators ->
        for pair in originators do
            match Provenance.originator records[pair.Key] with
            | Some(key, entry) ->
                Assert.Equal(str (get pair.Value "key"), key)
                Assert.Equal(str (get pair.Value "actorId"), entry.Actor.Id)
            | None -> failwith $"%s{pair.Key} has no originator"
    | _ -> failwith "no originators"

    match get expect "roles" with
    | :? JsonObject as roles ->
        for record in roles do
            match record.Value with
            | :? JsonObject as byRole ->
                for role in byRole do
                    let expected =
                        match role.Value with
                        | :? JsonArray as keys -> keys |> Seq.map str |> Seq.toList
                        | _ -> []

                    let actual = Provenance.withRole (Operation.ofCode role.Key) records[record.Key] |> List.map fst
                    Assert.Equal<string list>(expected, actual)
            | _ -> ()
    | _ -> failwith "no roles"

    // Lineage links records without merging their authors.
    let rec reach seen reference =
        match Map.tryFind reference records with
        | Some block ->
            block.DerivedFrom
            |> Option.defaultValue []
            |> List.filter (fun r -> not (Set.contains r seen))
            |> List.fold (fun acc r -> reach (Set.add r acc) r) seen
        | None -> seen

    let reached = reach Set.empty (str (get expect "lineageFrom"))

    match get expect "lineageReaches" with
    | :? JsonArray as targets ->
        for target in targets do
            Assert.Contains(str target, reached)
    | _ -> ()

    let codexKeys =
        records
        |> Map.toList
        |> List.collect (fun (_, block) -> block.Contributions)
        |> List.filter (fun (_, entry) -> entry.Actor.Id = str (get (get expect "distinctExecutionsOfOneAgent") "actorId"))
        |> List.map fst
        |> List.distinct
        |> List.sort

    match get (get expect "distinctExecutionsOfOneAgent") "keys" with
    | :? JsonArray as keys -> Assert.Equal<string list>(keys |> Seq.map str |> Seq.toList |> List.sort, codexKeys)
    | _ -> ()

    let originatorCount = records |> Map.toList |> List.choose (snd >> Provenance.originator) |> List.length
    Assert.Equal((get expect "chainOriginatorCount").GetValue<int>(), originatorCount)

    // Every replayed block is itself a supported interchange block.
    for _, block in Map.toList records do
        match ProvenanceJson.classify (ProvenanceJson.toNode block) with
        | ProvenanceJson.Supported _ -> ()
        | other -> failwith $"replayed block is not supported: %A{other}"
