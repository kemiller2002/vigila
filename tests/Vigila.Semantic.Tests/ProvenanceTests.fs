/// The append rules of the Praxis provenance contract, on the typed model
/// (VIG-PROV-003, VIG-PROV-004, VIG-PROV-005, VIG-PROV-008, VIG-PROV-015).
module Vigila.Semantic.ProvenanceTests

open Xunit
open Vigila.Semantic.Actors
open Vigila.Semantic.Provenance

let private codex = ProvenanceActor.agent "openai/codex" "openai" "gpt-5-codex" "codex"
let private claude = ProvenanceActor.agent "anthropic/claude-code" "anthropic" "unknown" "claude-code"

let private entry operations at actor =
    { Operations = operations
      At = at
      Last = None
      Actor = actor
      Reason = None
      Evidence = None
      Extensions = [] }

let private ok result =
    match result with
    | Ok(block, _) -> block
    | Error(message: string) -> failwith message

let private created =
    Provenance.empty
    |> Provenance.append "EXE-20260926T080000000Z-a1a1a1a1" (entry [ Operation.Created ] "2026-09-26T08:00:00.000Z" codex)
    |> ok

[<Fact>]
let ``two executions of one agent stay two contributions`` () =
    let block =
        created
        |> Provenance.append
            "EXE-20260926T100000000Z-a2a2a2a2"
            (entry [ Operation.Remediated ] "2026-09-26T10:00:00.000Z" codex)
        |> ok

    Assert.Equal(2, block.Contributions.Length)
    Assert.Equal(Some "EXE-20260926T080000000Z-a1a1a1a1", Provenance.originator block |> Option.map fst)
    Assert.Equal<string list>([ "EXE-20260926T100000000Z-a2a2a2a2" ], Provenance.withRole Operation.Remediated block |> List.map fst)

[<Fact>]
let ``a contribution is never re-attributed to another actor`` () =
    let result =
        created
        |> Provenance.append "EXE-20260926T080000000Z-a1a1a1a1" (entry [ Operation.Modified ] "2026-09-26T08:05:00.000Z" claude)

    Assert.True(Result.isError result)

[<Fact>]
let ``a second created is refused`` () =
    let result =
        created
        |> Provenance.append "EXE-20260926T090000000Z-b1b1b1b1" (entry [ Operation.Created ] "2026-09-26T09:00:00.000Z" claude)

    Assert.True(Result.isError result)

[<Fact>]
let ``a created that follows existing contributions is refused`` () =
    // A legacy item that gained provenance later has no creation; one cannot
    // be added after the fact (VIG-PROV-008).
    let legacy =
        Provenance.empty
        |> Provenance.append "EXE-20260926T090000000Z-b1b1b1b1" (entry [ Operation.Resolved ] "2026-09-26T09:00:00.000Z" claude)
        |> ok

    Assert.Equal(None, Provenance.originator legacy)

    let late =
        legacy
        |> Provenance.append "EXE-20260926T100000000Z-a2a2a2a2" (entry [ Operation.Created ] "2026-09-26T10:00:00.000Z" codex)

    Assert.True(Result.isError late)

[<Fact>]
let ``appending an identical contribution changes nothing`` () =
    match created |> Provenance.append "EXE-20260926T080000000Z-a1a1a1a1" (entry [ Operation.Created ] "2026-09-26T08:00:00.000Z" codex) with
    | Ok(block, changed) ->
        Assert.False changed
        Assert.Equal(created, block)
    | Error e -> failwith e

[<Fact>]
let ``the same execution merges operations and advances last`` () =
    match created |> Provenance.append "EXE-20260926T080000000Z-a1a1a1a1" (entry [ Operation.Modified ] "2026-09-26T08:30:00.000Z" codex) with
    | Ok(block, changed) ->
        Assert.True changed
        let _, merged = block.Contributions.Head
        Assert.Equal<Operation list>([ Operation.Created; Operation.Modified ], merged.Operations)
        Assert.Equal(Some "2026-09-26T08:30:00.000Z", merged.Last)
        Assert.Equal("2026-09-26T08:00:00.000Z", merged.At)
    | Error e -> failwith e

[<Fact>]
let ``an agent must be keyed by an execution`` () =
    let result =
        Provenance.empty
        |> Provenance.append "CTB-20260926-11111111" (entry [ Operation.Created ] "2026-09-26T08:00:00.000Z" codex)

    Assert.True(Result.isError result)

[<Fact>]
let ``a credential in a contribution is refused`` () =
    let leaky = ProvenanceActor.human "ghp_0123456789abcdefghijABCDEFGHIJ0123"

    let result =
        Provenance.empty
        |> Provenance.append "CTB-20260926-22222222" (entry [ Operation.Reviewed ] "2026-09-26T08:00:00.000Z" leaky)

    Assert.True(Result.isError result)

[<Fact>]
let ``an operation-only key is formed without inventing an execution`` () =
    Assert.Equal("EXT-op.op-1-2", Provenance.operationKey "op 1/2")
    Assert.Equal(ForeignExecution, Provenance.keyKind (Provenance.operationKey "op 1/2"))
    Assert.Equal(Ok "EXT-vigila.op-1", Provenance.foreignExecutionKey "vigila" "op-1")

[<Fact>]
let ``lineage is added without duplicates and never becomes authorship`` () =
    let block =
        created
        |> Provenance.addLineage [ "aegis:finding/SF-0001"; "aegis:finding/SF-0001" ]
        |> Provenance.addLineage [ "aegis:finding/SF-0001"; "git:commit/5e1f0c2" ]

    Assert.Equal(Some [ "aegis:finding/SF-0001"; "git:commit/5e1f0c2" ], block.DerivedFrom)
    Assert.Equal(1, block.Contributions.Length)

[<Theory>]
[<InlineData("agent", "Agent")>]
[<InlineData("human", "Human")>]
[<InlineData("automation", "AutomatedProcess")>]
[<InlineData("unknown", "Unknown")>]
[<InlineData("x-org-bot", "Unknown")>]
let ``the legacy projection follows the documented mapping`` (kind: string) (expected: string) =
    // VIG-PROV-015.
    let actor =
        { ProvenanceActor.unknown with
            Kind = Option.get (ActorKind.tryParse kind)
            Id = "someone" }

    let projected = ProvenanceActor.toLegacy actor
    Assert.Equal(expected, $"%A{projected.Type}")
    Assert.Equal("someone", projected.Name)

[<Fact>]
let ``an unknown actor projects as unknown, never as a guessed type`` () =
    let projected = ProvenanceActor.toLegacy ProvenanceActor.unknown
    Assert.Equal(ActorType.Unknown, projected.Type)
    Assert.Equal("unknown", projected.Name)
