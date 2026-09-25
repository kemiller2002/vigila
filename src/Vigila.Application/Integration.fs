/// Tier 3 - Echelon integration boundary.
///
/// Vigila owns translation from the ecosystem-level followup.create contract
/// into Vigila semantics. Callers never construct Vigila persistence artifacts.
module Vigila.Application.Integration

open System
open Vigila.Semantic.Actors
open Vigila.Semantic.Item
open Vigila.Semantic.Items
open Vigila.Semantic.Time
open Vigila.Semantic.Tags

type KnownValue =
    | Known of string
    | Unknown
    | NotApplicable

type IntegrationActor =
    { Kind: string
      Provider: KnownValue
      Identity: KnownValue
      RunId: KnownValue
      SessionId: KnownValue }

type IntegrationSource =
    { Repository: KnownValue
      Branch: KnownValue
      Commit: KnownValue
      WorkItem: KnownValue }

type ExecutionEnvelope =
    { OperationId: string
      CorrelationId: string
      Timestamp: Instant
      Actor: IntegrationActor
      Source: IntegrationSource option }

type RequestedAction =
    | Review
    | Decide
    | Approve
    | ProvideInformation
    | Investigate
    | Other

type Priority =
    | Low
    | Normal
    | High
    | Urgent

type FollowUpCreate =
    { Title: string
      Reason: string
      RequestedAction: RequestedAction
      Priority: Priority
      ReviewAfter: Instant option
      DueAt: Instant option
      Tags: string list }

type CreateRequest =
    { Envelope: ExecutionEnvelope
      FollowUp: FollowUpCreate }

type CreateResult =
    | Created of Item
    | Existing of Item
    | Rejected of string

/// Idempotency is a provider concern. The concrete host may persist this index
/// however it chooses; callers only supply the operation id.
type OperationIndex =
    abstract TryFind: operationId: string -> Item option
    abstract Record: operationId: string * item: Item -> unit

let private text = function
    | Known value -> Some value
    | Unknown
    | NotApplicable -> None

let private actorFrom envelope =
    let identity = text envelope.Actor.Identity
    let provider = text envelope.Actor.Provider
    let name =
        match identity, provider with
        | Some i, Some p -> p + ":" + i
        | Some i, None -> i
        | None, Some p -> p + ":unknown"
        | None, None -> "unknown-integration-actor"

    let actorType =
        match envelope.Actor.Kind.Trim().ToLowerInvariant() with
        | "agent" -> ActorType.Agent
        | "human" -> ActorType.Human
        | "automation" -> ActorType.AutomatedProcess
        | _ -> ActorType.Integration

    match Actor.create actorType name with
    | Ok actor -> actor
    | Error message -> invalidOp message

let private sourceReferences envelope =
    match envelope.Source with
    | None -> []
    | Some source ->
        match text source.Repository with
        | None -> []
        | Some repository ->
            let externalId =
                text source.WorkItem
                |> Option.orElseWith (fun () -> text source.Commit)

            [ { Type = "echelon-integration"
                DisplayName = repository
                ExternalId = externalId
                Url = None } ]

let private requestedActionText = function
    | Review -> "Review"
    | Decide -> "Decide"
    | Approve -> "Approve"
    | ProvideInformation -> "Provide information"
    | Investigate -> "Investigate"
    | Other -> "Follow up"

let private validate request =
    if String.IsNullOrWhiteSpace request.Envelope.OperationId then
        Error "operationId is required."
    elif String.IsNullOrWhiteSpace request.Envelope.CorrelationId then
        Error "correlationId is required."
    elif String.IsNullOrWhiteSpace request.FollowUp.Reason then
        Error "reason is required."
    else
        match Title.create request.FollowUp.Title, TagSet.ofStrings request.FollowUp.Tags with
        | Ok title, Ok tags -> Ok(title, tags)
        | Error titleError, _ -> Error titleError
        | _, Error tagErrors -> Error(String.concat " " tagErrors)

let create (clock: Clock) (index: OperationIndex) request =
    match validate request with
    | Error reason -> Rejected reason
    | Ok(title, tags) ->
        match index.TryFind request.Envelope.OperationId with
        | Some existing -> Existing existing
        | None ->
            let actor = actorFrom request.Envelope
            let initial = Item.create clock actor CreatedVia.Integration title

            let item =
                { initial with
                    Kind = ItemKind.FollowUp
                    Description = Some request.FollowUp.Reason
                    NextAction = Some(requestedActionText request.FollowUp.RequestedAction)
                    Due = request.FollowUp.DueAt |> Option.map WhenValue.AtInstant
                    FollowUp = request.FollowUp.ReviewAfter |> Option.map WhenValue.AtInstant
                    Sources = sourceReferences request.Envelope
                    Tags = tags
                    Important =
                        match request.FollowUp.Priority with
                        | High | Urgent -> true
                        | Low | Normal -> false
                    NeedsReview = true }

            index.Record(request.Envelope.OperationId, item)
            Created item