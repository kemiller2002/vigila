module Vigila.Application.Tests.IntegrationTests

open System
open Xunit
open Vigila.Application.Integration
open Vigila.Semantic.Identifiers
open Vigila.Semantic.Items
open Vigila.Semantic.Time

type MemoryIndex() =
    let items = Collections.Generic.Dictionary<string, Vigila.Semantic.Item.Item>()
    interface OperationIndex with
        member _.TryFind operationId =
            match items.TryGetValue operationId with
            | true, item -> Some item
            | _ -> None
        member _.Record(operationId, item) = items[operationId] <- item

let instant = Instant.ofDateTimeOffset(DateTimeOffset(2026, 9, 25, 15, 0, 0, TimeSpan.Zero))
let clock = Clock.fixedAt instant

let request operationId =
    { Envelope =
        { OperationId = operationId
          CorrelationId = "corr-15"
          Timestamp = instant
          Actor =
            { Kind = "agent"
              Provider = Known "openai"
              Identity = Known "gpt-5.6-sol"
              RunId = Known "run-15"
              SessionId = Unknown }
          Source =
            Some
                { Repository = Known "kemiller2002/vigila"
                  Branch = Known "feature/echelon-followup-integration-15"
                  Commit = Unknown
                  WorkItem = Known "15" } }
      FollowUp =
        { Title = "Review integration result"
          Reason = "Agent discovered a decision that needs human review."
          RequestedAction = Review
          Priority = Normal
          ReviewAfter = None
          DueAt = None
          Tags = [ "integration" ] } }

[<Fact>]
let ``create maps follow-up contract into Vigila semantics`` () =
    let index = MemoryIndex() :> OperationIndex
    match Integration.create clock index (request "op-1") with
    | Created item ->
        Assert.Equal(ItemKind.FollowUp, item.Kind)
        Assert.Equal("Review integration result", Title.value item.Title)
        Assert.Equal(Some "Agent discovered a decision that needs human review.", item.Description)
        Assert.Equal(Some "Review", item.NextAction)
        Assert.True(item.NeedsReview)
        Assert.Equal("openai:gpt-5.6-sol", item.CreatedBy.Name)
        Assert.Single(item.Sources) |> ignore
    | other -> failwithf "Expected Created, got %A" other

[<Fact>]
let ``replaying operation id returns existing item`` () =
    let index = MemoryIndex() :> OperationIndex
    let first = Integration.create clock index (request "op-retry")
    let second = Integration.create clock index (request "op-retry")
    match first, second with
    | Created created, Existing existing -> Assert.Equal(ItemId.toGuid created.Id, ItemId.toGuid existing.Id)
    | other -> failwithf "Expected Created then Existing, got %A" other

[<Fact>]
let ``unknown identity is preserved rather than fabricated`` () =
    let index = MemoryIndex() :> OperationIndex
    let baseRequest = request "op-unknown"
    let anonymous =
        { baseRequest with
            Envelope =
                { baseRequest.Envelope with
                    Actor =
                        { baseRequest.Envelope.Actor with
                            Provider = Unknown
                            Identity = Unknown } } }
    match Integration.create clock index anonymous with
    | Created item -> Assert.Equal("unknown-integration-actor", item.CreatedBy.Name)
    | other -> failwithf "Expected Created, got %A" other

[<Fact>]
let ``missing operation id is rejected without recording`` () =
    let index = MemoryIndex() :> OperationIndex
    match Integration.create clock index (request "   ") with
    | Rejected message -> Assert.Equal("operationId is required.", message)
    | other -> failwithf "Expected Rejected, got %A" other
