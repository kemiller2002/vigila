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
    Assert.Equal(Ok "EXT-op.op_201_2f2", Provenance.operationKey "op 1/2")
    Assert.Equal(Ok ForeignExecution, Provenance.operationKey "op 1/2" |> Result.map Provenance.keyKind)
    Assert.Equal(Ok "EXT-vigila.op-1", Provenance.foreignExecutionKey "vigila" "op-1")

[<Fact>]
let ``lineage is added without duplicates and never becomes authorship`` () =
    let block =
        created
        |> Provenance.addLineage [ "aegis:finding/SF-0001"; "aegis:finding/SF-0001" ]
        |> Result.bind (fst >> Provenance.addLineage [ "aegis:finding/SF-0001"; "git:commit/5e1f0c2" ])
        |> ok

    Assert.Equal(Some [ "aegis:finding/SF-0001"; "git:commit/5e1f0c2" ], block.DerivedFrom)
    Assert.Equal(1, block.Contributions.Length)

[<Fact>]
let ``adding lineage that is already there changes nothing`` () =
    let once = created |> Provenance.addLineage [ "RQ-1" ] |> ok
    Assert.Equal(Ok(once, false), Provenance.addLineage [ "RQ-1" ] once)

[<Theory>]
[<InlineData("ghp_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")>]
[<InlineData("see Bearer abcdefghijklmnopqrstuvwxyz")>]
[<InlineData(" \t")>]
[<InlineData("")>]
let ``lineage that is blank, credential-like or not well-formed is refused`` (reference: string) =
    // Contract 1.2 rule 3; review finding 1: lineage was added unchecked.
    Assert.True(Result.isError (Provenance.addLineage [ "RQ-1"; reference ] created))

[<Fact>]
let ``lineage is never added to a malformed block`` () =
    let broken = { created with Contributions = created.Contributions @ [ "bad", snd created.Contributions.Head ] }
    Assert.True(Result.isError (Provenance.addLineage [ "RQ-1" ] broken))

[<Theory>]
[<InlineData("\u0085")>]
[<InlineData("\uFEFF")>]
[<InlineData("\u001C")>]
[<InlineData("\u00A0")>]
let ``only ASCII whitespace is blank`` (text: string) =
    // Contract 1.2 rule 2: .NET Trim() would call these blank; the contract
    // counts them as content.
    Assert.False(ContractText.isBlank text)
    Assert.True(Result.isOk (Provenance.addLineage [ text ] created))
    let actor = ProvenanceActor.human text
    Assert.Empty(Provenance.actorProblems "actor" actor)

[<Fact>]
let ``ASCII whitespace is blank`` () =
    Assert.True(ContractText.isBlank " \t\n\u000B\u000C\r")
    Assert.Equal("a b", ContractText.asciiTrim "\t a b \r\n")
    Assert.NotEmpty(Provenance.actorProblems "actor" (ProvenanceActor.human " \t"))

[<Theory>]
[<InlineData("Bearer abcdefghijklmnop", true)>]
[<InlineData("BEARER\tabcdefghijklmnop", true)>]
[<InlineData("x:bearer abcdefghijklmnop", true)>]
[<InlineData("\u00e9bearer abcdefghijklmnop", true)>]
[<InlineData("mybearer abcdefghijklmnop", false)>]
[<InlineData("my_bearer abcdefghijklmnop", false)>]
[<InlineData("bearer\u0085abcdefghijklmnop", false)>]
[<InlineData("bearer \u212A\u212A\u212A\u212A\u212A\u212A\u212A\u212A\u212A\u212A\u212A\u212A\u212A\u212A\u212A\u212A\u212A\u212A\u212A\u212A", false)>]
let ``the bearer pattern uses ASCII classes only`` (text: string) (expected: bool) =
    // Contract 1.2 rule 2 / review finding 11: no \b, \s or case folding.
    Assert.Equal(expected, Provenance.isCredentialLike text)

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

// --- contract revision 1.1 (kemiller2002/praxis@c2657ef) -------------------

[<Fact>]
let ``keys, codes and kinds are matched exactly: a trailing newline is not accepted`` () =
    Assert.Equal(InvalidKey, Provenance.keyKind "EXE-20260926T080000000Z-a1a1a1a1\n")
    Assert.Equal(InvalidKey, Provenance.keyKind "EXT-dokimos.run-1\n")
    Assert.Equal(None, ActorKind.tryParse "x-bot\n")

    let result =
        created
        |> Provenance.append "EXE-20260926T090000000Z-b1b1b1b1" (entry [ Operation.Other "modified\n" ] "2026-09-26T09:00:00.000Z" claude)

    Assert.True(Result.isError result)

[<Theory>]
[<InlineData("2026-02-30T08:00:00.000Z", false)>]
[<InlineData("2026-09-26T24:00:00.000Z", false)>]
[<InlineData("0000-01-01T00:00:00.000Z", false)>]
[<InlineData("2026-09-26T08:00:00.000Z\n", false)>]
[<InlineData("2024-02-29T08:00:00Z", true)>]
[<InlineData("9999-12-31T23:59:59.999999999Z", true)>]
let ``timestamps must be calendar-valid`` (text: string) (valid: bool) =
    Assert.Equal(valid, Provenance.isTimestamp text)

[<Fact>]
let ``ordering is at millisecond precision, so sub-millisecond differences do not reorder`` () =
    // .0009 and .0001 are the same millisecond: the review does not precede
    // the creation.
    let block =
        Provenance.empty
        |> Provenance.append "EXE-20260926T080000000Z-a1a1a1a1" (entry [ Operation.Created ] "2026-09-26T08:00:00.0009Z" codex)
        |> ok
        |> Provenance.append "CTB-20260926-eeeeeeee" (entry [ Operation.Reviewed ] "2026-09-26T08:00:00.0001Z" (ProvenanceActor.human "kevin"))

    Assert.True(Result.isOk block)

[<Fact>]
let ``an append never returns a block that would be malformed`` () =
    // A contribution dated before the creation is refused rather than stored.
    let early =
        created
        |> Provenance.append "EXE-20260926T070000000Z-b1b1b1b1" (entry [ Operation.Modified ] "2026-09-26T07:00:00.000Z" claude)

    Assert.True(Result.isError early)

[<Fact>]
let ``created cannot be merged into an entry that earlier contributions precede`` () =
    let block =
        Provenance.empty
        |> Provenance.append "EXE-20260926T080000000Z-b1b1b1b1" (entry [ Operation.Reviewed ] "2026-09-26T08:00:00.000Z" claude)
        |> ok
        |> Provenance.append "EXE-20260926T090000000Z-a1a1a1a1" (entry [ Operation.Modified ] "2026-09-26T09:00:00.000Z" codex)
        |> ok

    let result =
        block
        |> Provenance.append "EXE-20260926T090000000Z-a1a1a1a1" (entry [ Operation.Created ] "2026-09-26T09:30:00.000Z" codex)

    Assert.True(Result.isError result)

[<Fact>]
let ``a same-key merge keeps incoming unknown fields, the existing entry winning on conflict`` () =
    let existing =
        { entry [ Operation.Created ] "2026-09-26T08:00:00.000Z" codex with
            Extensions = [ "x-a", RawJson "1" ] }

    let incoming =
        { entry [ Operation.Modified ] "2026-09-26T08:30:00.000Z" codex with
            Extensions = [ "x-a", RawJson "2"; "x-b", RawJson "true" ] }

    let block =
        Provenance.empty
        |> Provenance.append "EXE-20260926T080000000Z-a1a1a1a1" existing
        |> ok
        |> Provenance.append "EXE-20260926T080000000Z-a1a1a1a1" incoming
        |> ok

    let merged = snd block.Contributions.Head
    Assert.Equal<(string * RawJson) list>([ "x-a", RawJson "1"; "x-b", RawJson "true" ], merged.Extensions)

[<Fact>]
let ``a same-key merge takes the later of the two last times`` () =
    let incoming =
        { entry [ Operation.Modified ] "2026-09-26T08:10:00.000Z" codex with
            Last = Some "2026-09-26T09:00:00.000Z" }

    let merged =
        created |> Provenance.append "EXE-20260926T080000000Z-a1a1a1a1" incoming |> ok |> fun b -> snd b.Contributions.Head

    Assert.Equal(Some "2026-09-26T09:00:00.000Z", merged.Last)

[<Fact>]
let ``an actor of unknown identity cannot extend an entry a known actor holds`` () =
    let anonymous = { codex with Id = "unknown" }
    // `unknown` never contradicts, so the actors "agree"; it still never proves
    // the same run.
    Assert.True(Provenance.actorsAgree codex anonymous)

    let result =
        created
        |> Provenance.append "EXE-20260926T080000000Z-a1a1a1a1" (entry [ Operation.Modified ] "2026-09-26T08:30:00.000Z" anonymous)

    Assert.True(Result.isError result)

[<Theory>]
[<InlineData("op 1", "op_201")>]
[<InlineData("gh/99", "gh_2f99")>]
[<InlineData("a_b", "a_5fb")>]
[<InlineData("a-b.c", "a-b_2ec")>]
[<InlineData("vigila.7", "vigila_2e7")>]
[<InlineData("é", "_c3_a9")>]
[<InlineData("op-\U0001F600", "op-_f0_9f_98_80")>]
[<InlineData("op-\U0001F601", "op-_f0_9f_98_81")>]
[<InlineData("op-\uFFFD", "op-_ef_bf_bd")>]
let ``key segments are escaped per code point`` (text: string) (expected: string) =
    // Contract 1.2 rule 4 / review finding 6: '.' is escaped, and characters
    // outside the BMP are escaped as one code point, never as two halves.
    Assert.Equal(Ok expected, Provenance.escapeKeySegment text)

[<Fact>]
let ``escaping is injective where replacement was not`` () =
    // Under the old '-' replacement all three collapsed to "a-b".
    let escaped = [ "a b"; "a/b"; "a-b"; "a_2fb"; "a.b"; "a_2eb" ] |> List.map Provenance.escapeKeySegment
    Assert.Equal(escaped.Length, (List.distinct escaped).Length)

[<Fact>]
let ``an id that is empty or not well-formed Unicode cannot form a key`` () =
    // Built in code: xUnit replaces a lone surrogate in attribute data.
    for text in [ ""; "op-" + string (char 0xD83D); string (char 0xDE00) + "x" ] do
        Assert.True(Result.isError (Provenance.escapeKeySegment text))
        Assert.True(Result.isError (Provenance.operationKey text))

[<Fact>]
let ``lineage holding an unpaired surrogate is refused`` () =
    Assert.True(Result.isError (Provenance.addLineage [ "op-" + string (char 0xD800) ] created))
    Assert.True(ContractText.hasLoneSurrogate ("a" + string (char 0xDC00)))
    Assert.False(ContractText.hasLoneSurrogate "op-\U0001F600")
