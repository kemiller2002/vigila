/// Tier 1 identity rules: the actor, the type-to-kind mapping, and run keys.
///
/// Requirements: VIG-DOM-050, VIG-DOM-051, VIG-DOM-052.
module Vigila.Semantic.ProvenanceTests

open Xunit
open Vigila.Semantic.Actors
open Vigila.Semantic.Provenance

let private ok result =
    match result with
    | Ok value -> value
    | Error(e: string) -> failwith e

[<Fact>]
let ``a human has no provider, model or runtime`` () =
    // VIG-DOM-050: they do not apply, which is different from unknown.
    Assert.Equal(None, (ok (Actor.human "kevin")).Tooling)
    Assert.True(Actor.identify Human "kevin" "openai" "gpt-5" "codex" |> Result.isError)

[<Fact>]
let ``a non-human records unknown rather than guessing`` () =
    Assert.Equal(Some Tooling.unknown, (ok (Actor.agent "codex")).Tooling)

    let declared = ok (Actor.identify Agent "acme-planner" "acme" null "acme-cli")
    Assert.Equal(Some { Provider = "acme"; Model = "unknown"; Runtime = "acme-cli" }, declared.Tooling)

[<Fact>]
let ``identity never carries a credential`` () =
    Assert.True(Actor.identify Agent "anthropic/claude-code" "anthropic" "sk-ant-api03-AAAAAAAAAAAAAAAAAAAAAAAA" "claude-code" |> Result.isError)

[<Fact>]
let ``every Vigila type maps to a Praxis kind and back`` () =
    // VIG-DOM-051: the mapping is lossless, including Integration.
    for actorType in [ Human; Agent; AutomatedProcess; Integration ] do
        let kind = PraxisActor.kindOf actorType
        Assert.True(PraxisActor.isKind kind)
        Assert.Equal(Some actorType, PraxisActor.typeOf kind (PraxisActor.vigilaTypeOf actorType))

[<Fact>]
let ``kinds Vigila has no type for are never coerced into one`` () =
    Assert.Equal(None, PraxisActor.typeOf "unknown" None)
    Assert.Equal(None, PraxisActor.typeOf "x-bot" None)
    Assert.Equal(None, PraxisActor.typeOf "agent" (Some "integration"))

[<Fact>]
let ``Vigila's own runs are namespaced and cannot pass for a Praxis execution`` () =
    // RQ-ROS-2026-A014 via VIG-DOM-052.
    let key = ok (ContributionKey.foreignExecution SystemName "gh-run-9001")
    Assert.Equal("EXE-vigila.gh-run-9001", ContributionKey.value key)
    Assert.True(ContributionKey.isForeign key)
    Assert.False(ContributionKey.isForeign (ok (ContributionKey.parse "EXE-20260926T140000000Z-e5e5e5e5")))
    Assert.True(ContributionKey.foreignExecution "Vigila" "x" |> Result.isError)

[<Fact>]
let ``a key is an execution or a contribution, nothing else`` () =
    Assert.True(ContributionKey.parse "RUN-123" |> Result.isError)
    Assert.Equal(ContributionKey.Outside "CTB-20260926-5f2e19aa", ok (ContributionKey.parse "CTB-20260926-5f2e19aa"))

[<Fact>]
let ``an agent acts within an execution`` () =
    let codex = ok (Actor.identify Agent "openai/codex" "openai" "gpt-5-codex" "codex")
    Assert.True(Attribution.create codex (Some(ContributionKey.Outside "CTB-20260926-1")) |> Result.isError)
    Assert.True(Attribution.create codex (Some(ContributionKey.Execution "EXE-vigila.1")) |> Result.isOk)
    // Unknown is allowed, and stays unknown.
    Assert.Equal(None, (ok (Attribution.create codex None)).Execution)
