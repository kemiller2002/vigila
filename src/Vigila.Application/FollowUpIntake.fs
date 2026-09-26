/// Tier 3 - `followup.create` intake at the Echelon integration boundary.
///
/// Turns an Echelon execution envelope plus a `followup.create` v1 payload into
/// a follow-up item, without losing or merging anyone's identity:
///
///   * the invoking actor -- the generating system or the requesting agent or
///     human, taken only from the envelope -- is the item's `created`
///     contribution, keyed by `envelope.execution` or `EXT-op.<operationId>`
///     (VIG-PROV-009);
///   * Vigila records its own `transformed` contribution as `echelon/vigila`,
///     keyed `EXT-vigila.<operationId>` (VIG-PROV-012);
///   * `envelope.provenance` -- the payload's upstream origin, such as the
///     agent that discovered a finding -- is classified, then kept verbatim as
///     the item's `receivedProvenance`. Its lineage, plus the request's source
///     reference, becomes the item's `derivedFrom`. It is never merged into the
///     item's own contributions, so an upstream author cannot become the
///     follow-up's author (VIG-PROV-010).
///
/// Pure: the timestamp comes from the envelope, and the item id is derived
/// from the operation id, so replaying a request yields the same item
/// (VIG-PROV-011). The caller supplies a lookup for items already stored.
///
/// Requirements: VIG-PROV-009, VIG-PROV-010, VIG-PROV-011, VIG-PROV-012,
/// VIG-PROV-013, VIG-PROV-014, VIG-AGT-035, VIG-AGT-050.
module Vigila.Application.FollowUpIntake

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Vigila.Semantic.Identifiers
open Vigila.Semantic.Time
open Vigila.Semantic.Actors
open Vigila.Semantic.Items
open Vigila.Semantic.Tags
open Vigila.Semantic.History
open Vigila.Semantic.Item
open Vigila.Semantic.Provenance

[<Literal>]
let EnvelopeV1 = "echelon.execution-envelope/v1"

[<Literal>]
let EnvelopeV2 = "echelon.execution-envelope/v2"

/// A refused request, with a stable code from VIG-AGT-050 and every problem
/// named, so an agent never has to parse prose.
type IntakeError =
    { Code: string
      Problems: string list }

/// The parts of an execution envelope Vigila uses.
type Envelope =
    { Schema: string
      OperationId: string
      CorrelationId: string
      Timestamp: Instant
      /// The current invoking actor, as a Praxis actor (v1 actors are mapped).
      Actor: ProvenanceActor
      /// `envelope.execution`, the v1 run key, or `EXT-op.<operationId>`.
      ContributionKey: string
      /// `envelope.provenance`, already classified; never malformed here.
      Provenance: ProvenanceJson.Verdict option }

/// Where the follow-up came from, read from `context.source`.
type SourceLink =
    { Ref: string
      Url: string option
      DisplayName: string option }

/// A validated `followup.create` v1 payload.
type FollowUpRequest =
    { Title: Title
      Reason: string
      RequestedAction: string
      Priority: string
      ReviewAfter: Instant option
      DueAt: Instant option
      Tags: TagSet
      Source: SourceLink option }

type IntakeOptions =
    { /// Record Vigila's own `transformed` contribution (VIG-PROV-012).
      RecordTransformation: bool }

let defaultOptions = { RecordTransformation = true }

/// The result of a successful intake.
[<NoComparison>]
type Intake =
    { Item: Item
      /// True when the operation id had already been processed and the stored
      /// item is returned unchanged.
      Replayed: bool
      /// Tolerated forward-compatible content in the received provenance.
      Warnings: string list }

let private validationFailed problems =
    Error { Code = "ValidationFailed"; Problems = problems }

let private field (o: JsonObject) (name: string) : (JsonNode | null) option =
    let mutable found: JsonNode | null = null
    if o.TryGetPropertyValue(name, &found) then Some found else None

let private stringOf (node: JsonNode | null) =
    match node with
    | :? JsonValue as value when value.GetValueKind() = JsonValueKind.String -> Some(value.GetValue<string>())
    | _ -> None

let private requiredText (o: JsonObject) prefix name =
    match field o name |> Option.bind stringOf with
    | Some text when text.Trim().Length > 0 -> Ok text
    | _ -> Error $"%s{prefix}.%s{name} must be a non-empty string"

let private parseObject label (json: string) =
    try
        match JsonNode.Parse json with
        | :? JsonObject as o -> Ok o
        | _ -> Error $"%s{label} must be a JSON object"
    with :? JsonException as ex ->
        Error $"%s{label} is not valid JSON: %s{ex.Message}"

let private collect results =
    results |> List.choose (function Error e -> Some e | Ok _ -> None)

// --- envelope ---------------------------------------------------------------

let private rfc3339 =
    System.Text.RegularExpressions.Regex(
        "^[0-9]{4}-[0-9]{2}-[0-9]{2}[Tt][0-9]{2}:[0-9]{2}:[0-9]{2}(\\.[0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})\\z",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
    )

/// An RFC 3339 date-time (JSON Schema `format: date-time`), not whatever
/// `DateTimeOffset` would accept.
let private parseDateTime (text: string) =
    if rfc3339.IsMatch text then Instant.parse text else Error $"'%s{text}' is not an RFC 3339 date-time"

let private extensionProperty =
    System.Text.RegularExpressions.Regex("^x-[a-z0-9][a-z0-9-]*\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant)

let private envelopeV1Fields = [ "schema"; "operationId"; "correlationId"; "timestamp"; "actor"; "source" ]
let private envelopeV2Fields = envelopeV1Fields @ [ "execution"; "provenance" ]
let private sourceFields = [ "repository"; "branch"; "commit"; "workItem" ]
let private v1ActorFields = [ "kind"; "provider"; "identity"; "runId"; "sessionId" ]

/// A v1 `knownValue`: `{state: known|unknown|not-applicable, value?: string}`.
let private knownValueProblems (path: string) (node: JsonNode | null) =
    match node with
    | :? JsonObject as o ->
        [ for pair in o do
              if pair.Key <> "state" && pair.Key <> "value" then
                  $"%s{path}.%s{pair.Key} is not a knownValue field"
          match field o "state" |> Option.bind stringOf with
          | Some("known" | "unknown" | "not-applicable") -> ()
          | _ -> $"%s{path}.state must be known, unknown or not-applicable"
          match field o "value" with
          | None -> ()
          | Some node when (stringOf node).IsSome -> ()
          | Some _ -> $"%s{path}.value must be a string" ]
    | _ -> [ $"%s{path} must be a knownValue object" ]

/// The registry's structural rules for an envelope
/// (`schemas/execution-envelope.schema.json`, `execution-envelope.v2.schema.json`
/// in echelon-registry): no property outside the schema, except `x-...`
/// extension properties on v2; `source` and the v1 actor use `knownValue`.
/// Vigila adopts the registry's rule rather than tolerating unknown
/// properties, so a misspelt field is reported instead of silently ignored.
let private structureProblems (tag: string) (o: JsonObject) =
    let allowed = if tag = EnvelopeV2 then envelopeV2Fields else envelopeV1Fields @ [ "provenance" ]

    [ for pair in o do
          if not (List.contains pair.Key allowed) && not (tag = EnvelopeV2 && extensionProperty.IsMatch pair.Key) then
              $"envelope.%s{pair.Key} is not a %s{tag} property"
      match field o "source" with
      | None -> ()
      | Some(:? JsonObject as source) ->
          for pair in source do
              if List.contains pair.Key sourceFields then
                  yield! knownValueProblems $"envelope.source.%s{pair.Key}" pair.Value
              else
                  $"envelope.source.%s{pair.Key} is not a source property"
      | Some _ -> "envelope.source must be an object"
      if tag = EnvelopeV1 then
          match field o "actor" with
          | Some(:? JsonObject as actor) ->
              for pair in actor do
                  if pair.Key = "kind" then ()
                  elif List.contains pair.Key v1ActorFields then
                      yield! knownValueProblems $"envelope.actor.%s{pair.Key}" pair.Value
                  else
                      $"envelope.actor.%s{pair.Key} is not a v1 actor property"
              for required in [ "provider"; "identity" ] do
                  if (field actor required).IsNone then
                      $"envelope.actor.%s{required} is required in a v1 envelope"
          | _ -> () ]

/// `actorFromEnvelopeV1` from the reference library: `system` becomes
/// `automation`; only a `known` value is used, anything else is the literal
/// "unknown"; model and runtime, which v1 cannot express, are "unknown".
let actorFromEnvelopeV1 (actor: JsonObject) =
    let value name =
        match field actor name with
        | Some(:? JsonObject as known) when (field known "state" |> Option.bind stringOf) = Some "known" ->
            match field known "value" |> Option.bind stringOf with
            | Some text when text.Trim().Length > 0 -> text
            | _ -> ProvenanceActor.UnknownValue
        | _ -> ProvenanceActor.UnknownValue

    match field actor "kind" |> Option.bind stringOf with
    | Some "human" -> Ok(ProvenanceActor.human (value "identity"))
    | Some("agent" | "automation" | "system" as kind) ->
        Ok
            { Kind = if kind = "agent" then ActorKind.Agent else ActorKind.Automation
              Id = value "identity"
              Provider = Some(value "provider")
              Model = Some ProvenanceActor.UnknownValue
              Runtime = Some ProvenanceActor.UnknownValue
              Extensions = [] }
    | _ -> Error "envelope.actor.kind must be agent, human, automation or system"

/// `keyFromEnvelopeV1`, namespaced as echelon-registry REG-PROV-008 requires
/// (`keyFromEnvelopeV1Namespaced`):
///
///   * run id and `source.repository` both known ->
///     `EXT-run.<repository>.<runId>`, the repository escaped with `.` as `_2e`;
///   * run id known -> `EXT-run.<runId>`;
///   * otherwise `EXT-op.<operationId>`.
///
/// Ids are escaped injectively (`Provenance.escapeSegment`, contract 1.1), so
/// two different senders or runs never share a key.
let keyFromEnvelopeV1 (envelope: JsonObject) =
    let known (node: (JsonNode | null) option) =
        match node with
        | Some(:? JsonObject as value) when (field value "state" |> Option.bind stringOf) = Some "known" ->
            match field value "value" |> Option.bind stringOf with
            | Some text when text.Trim().Length > 0 -> Some text
            | _ -> None
        | _ -> None

    let run =
        match field envelope "actor" with
        | Some(:? JsonObject as actor) -> known (field actor "runId")
        | _ -> None

    let repository =
        match field envelope "source" with
        | Some(:? JsonObject as source) -> known (field source "repository")
        | _ -> None

    let operationId = field envelope "operationId" |> Option.bind stringOf |> Option.defaultValue ""

    match run, repository with
    | Some run, Some repository ->
        $"EXT-run.%s{Provenance.escapeSegment false repository}.%s{Provenance.escapeSegment true run}"
    | Some run, None -> $"EXT-run.%s{Provenance.escapeSegment true run}"
    | None, _ -> Provenance.operationKey operationId

let private parseEnvelope (o: JsonObject) =
    let schema = field o "schema" |> Option.bind stringOf

    match schema with
    | Some tag when tag <> EnvelopeV1 && tag <> EnvelopeV2 ->
        Error
            { Code = "SchemaUnsupported"
              Problems = [ $"envelope schema '%s{tag}' is not %s{EnvelopeV1} or %s{EnvelopeV2}" ] }
    | None -> validationFailed [ "envelope.schema is required" ]
    | Some tag ->
        let operationId = requiredText o "envelope" "operationId"
        let correlationId = requiredText o "envelope" "correlationId"

        let timestamp =
            requiredText o "envelope" "timestamp"
            |> Result.bind (fun text ->
                parseDateTime text |> Result.mapError (fun _ -> "envelope.timestamp must be an RFC 3339 timestamp"))

        let actorNode =
            match field o "actor" with
            | Some(:? JsonObject as actor) -> Ok actor
            | _ -> Error "envelope.actor must be an object"

        // The actor is outside the provenance block, so the tripwire runs on
        // it separately (VIG-PROV-013).
        let actorSecrets =
            match actorNode with
            | Ok actor ->
                ProvenanceJson.credentialFindings actor
                |> List.map (fun path -> $"envelope.actor.%s{path}: credential-like value; an actor must never carry authentication material")
            | Error _ -> []

        let actor =
            actorNode
            |> Result.bind (fun node ->
                if tag = EnvelopeV2 then
                    ProvenanceJson.parseActor "envelope.actor" node |> Result.mapError (String.concat "; ")
                else
                    actorFromEnvelopeV1 node)

        let key =
            match tag, field o "execution" with
            | EnvelopeV2, None -> operationId |> Result.map Provenance.operationKey
            | EnvelopeV2, Some node ->
                match stringOf node with
                | Some execution when
                    (match Provenance.keyKind execution with
                     | Execution
                     | ForeignExecution -> true
                     | _ -> false)
                    ->
                    Ok execution
                | _ -> Error "envelope.execution must be EXE-... or EXT-<system>.<run-id>"
            | _, Some _ -> Error "envelope.execution is not part of a v1 envelope"
            | _ ->
                match actorNode, operationId with
                | Ok _, Ok _ -> Ok(keyFromEnvelopeV1 o)
                | _ -> Error "envelope.actor and envelope.operationId are required"

        let provenance =
            match tag, field o "provenance" with
            | _, None -> Ok None
            | EnvelopeV1, Some _ -> Error [ "envelope.provenance requires an echelon.execution-envelope/v2 envelope" ]
            | _, Some node ->
                match ProvenanceJson.classify node with
                | ProvenanceJson.Malformed problems -> Error(problems |> List.map (sprintf "envelope.provenance: %s"))
                | verdict -> Ok(Some verdict)

        let problems =
            collect [ Result.map ignore operationId
                      Result.map ignore correlationId
                      Result.map ignore timestamp
                      Result.map ignore actor
                      Result.map ignore key ]
            @ structureProblems tag o
            @ actorSecrets
            @ (match provenance with Error p -> p | Ok _ -> [])

        match operationId, correlationId, timestamp, actor, key, provenance with
        | Ok operationId, Ok correlationId, Ok timestamp, Ok actor, Ok key, Ok provenance when problems.IsEmpty ->
            Ok
                { Schema = tag
                  OperationId = operationId
                  CorrelationId = correlationId
                  Timestamp = timestamp
                  Actor = actor
                  ContributionKey = key
                  Provenance = provenance }
        | _ -> validationFailed problems

/// Parses and validates an envelope (v2, or v1 through the Praxis mapping).
let readEnvelope (json: string) =
    parseObject "envelope" json
    |> Result.mapError (fun problem -> { Code = "ValidationFailed"; Problems = [ problem ] })
    |> Result.bind parseEnvelope

// --- payload ----------------------------------------------------------------

let private payloadFields =
    [ "title"; "reason"; "requestedAction"; "priority"; "reviewAfter"; "dueAt"; "tags"; "context" ]

let private requestedActions =
    [ "review"; "decide"; "approve"; "provide-information"; "investigate"; "other" ]

let private priorities = [ "low"; "normal"; "high"; "urgent" ]

let private optionalInstant (o: JsonObject) name =
    match field o name with
    | None -> Ok None
    | Some Null -> Ok None
    | Some node ->
        match stringOf node with
        | Some text -> parseDateTime text |> Result.map Some |> Result.mapError (fun _ -> $"payload.%s{name} must be a date-time")
        | None -> Error $"payload.%s{name} must be a date-time or null"

let private readSource (o: JsonObject) =
    match field o "context" with
    | None
    | Some Null -> Ok None
    | Some(:? JsonObject as context) ->
        match field context "source" with
        | None
        | Some Null -> Ok None
        | Some(:? JsonObject as source) ->
            match field source "ref" |> Option.bind stringOf with
            | Some reference when reference.Trim().Length > 0 ->
                let optional name = field source name |> Option.bind stringOf
                Ok(Some { Ref = reference.Trim(); Url = optional "url"; DisplayName = optional "displayName" })
            | _ -> Error "payload.context.source.ref must be a non-empty string such as 'aegis:finding/SF-0001'"
        | Some _ -> Error "payload.context.source must be an object"
    | Some _ -> Error "payload.context must be an object or null"

let private parsePayload (o: JsonObject) =
    let unknown =
        o
        |> Seq.map (fun pair -> pair.Key)
        |> Seq.filter (fun name -> not (List.contains name payloadFields))
        |> Seq.map (fun name -> $"payload.%s{name} is not a followup.create v1 field")
        |> Seq.toList

    let title =
        requiredText o "payload" "title"
        |> Result.bind (fun text ->
            if text.Trim().Length > 240 then
                Error "payload.title may be at most 240 characters"
            else
                Title.create text)

    let reason =
        requiredText o "payload" "reason"
        |> Result.bind (fun text ->
            if text.Length > Item.MaxDescriptionLength then
                Error $"payload.reason may be at most %d{Item.MaxDescriptionLength} characters"
            else
                Ok text)

    let oneOf name allowed =
        match field o name |> Option.bind stringOf with
        | Some value when List.contains value allowed -> Ok value
        | _ -> Error $"""payload.%s{name} must be one of: %s{String.concat ", " allowed}"""

    let requestedAction = oneOf "requestedAction" requestedActions
    let priority = oneOf "priority" priorities
    let reviewAfter = optionalInstant o "reviewAfter"
    let dueAt = optionalInstant o "dueAt"

    let tags =
        match field o "tags" with
        | None -> Ok TagSet.empty
        | Some(:? JsonArray as items) ->
            let texts = items |> Seq.map stringOf |> Seq.toList

            if texts |> List.exists Option.isNone then
                Error "payload.tags must be an array of strings"
            elif (List.distinct texts).Length <> texts.Length then
                Error "payload.tags must not repeat a tag"
            else
                TagSet.ofStrings (texts |> List.choose id)
                |> Result.mapError (fun errors -> "payload.tags: " + String.concat "; " errors)
        | Some _ -> Error "payload.tags must be an array of strings"

    let source = readSource o

    let problems =
        unknown
        @ collect [ Result.map ignore title
                    Result.map ignore reason
                    Result.map ignore requestedAction
                    Result.map ignore priority
                    Result.map ignore reviewAfter
                    Result.map ignore dueAt
                    Result.map ignore tags
                    Result.map ignore source ]

    match title, reason, requestedAction, priority, reviewAfter, dueAt, tags, source with
    | Ok title, Ok reason, Ok requestedAction, Ok priority, Ok reviewAfter, Ok dueAt, Ok tags, Ok source when
        problems.IsEmpty
        ->
        Ok
            { Title = title
              Reason = reason
              RequestedAction = requestedAction
              Priority = priority
              ReviewAfter = reviewAfter
              DueAt = dueAt
              Tags = tags
              Source = source }
    | _ -> validationFailed problems

/// Parses and validates a `followup.create` v1 payload.
let readPayload (json: string) =
    parseObject "payload" json
    |> Result.mapError (fun problem -> { Code = "ValidationFailed"; Problems = [ problem ] })
    |> Result.bind parsePayload

// --- building the item ------------------------------------------------------

/// The item id for an operation: a name-based (RFC 9562 version 8) GUID from
/// the operation id, so a replay addresses the same item. It is derived from
/// the command's identity, never from title text or position (VIG-PER-041).
let itemIdFor (operationId: string) =
    let hash = SHA256.HashData(Encoding.UTF8.GetBytes("vigila:followup.create:" + operationId))
    let bytes = Array.sub hash 0 16
    bytes[7] <- (bytes[7] &&& 0x0Fuy) ||| 0x80uy
    bytes[8] <- (bytes[8] &&& 0x3Fuy) ||| 0x80uy
    ItemId.ofGuid (Guid(bytes))

/// The key of Vigila's own `transformed` contribution (VIG-PROV-012).
let transformationKey (operationId: string) =
    Provenance.foreignExecutionKey "vigila" (Provenance.safeSegment operationId)

let private nextActionFor requestedAction =
    match requestedAction with
    | "review" -> Some "Review"
    | "decide" -> Some "Decide"
    | "approve" -> Some "Approve"
    | "provide-information" -> Some "Provide information"
    | "investigate" -> Some "Investigate"
    | _ -> None

/// The item's own provenance: the invoking actor's `created`, then Vigila's
/// `transformed`, then lineage. Lineage only ever comes from a block this
/// version can read; an unsupported block is never interpreted.
let private ownProvenance options (envelope: Envelope) (request: FollowUpRequest) =
    let created = Provenance.contribution [ Operation.Created ] envelope.Timestamp envelope.Actor

    let transformed =
        if options.RecordTransformation then
            transformationKey envelope.OperationId
            |> Result.map (fun key ->
                Some(
                    key,
                    { Provenance.contribution [ Operation.Transformed ] envelope.Timestamp ProvenanceActor.vigila with
                        Reason = Some "Vigila turned a followup.create request into a follow-up item" }
                ))
        else
            Ok None

    let lineage =
        (match envelope.Provenance with
         | Some(ProvenanceJson.Supported(block, _)) -> Option.defaultValue [] block.DerivedFrom
         | _ -> [])
        @ (request.Source |> Option.map (fun source -> source.Ref) |> Option.toList)

    Provenance.empty
    |> Provenance.append envelope.ContributionKey created
    |> Result.bind (fun (block, _) ->
        transformed
        |> Result.bind (function
            | Some(key, entry) -> Provenance.append key entry block |> Result.map fst
            | None -> Ok block))
    |> Result.map (Provenance.addLineage lineage)

let private sourceReference (link: SourceLink) =
    let system =
        match link.Ref.IndexOf ':' with
        | index when index > 0 -> link.Ref.Substring(0, index)
        | _ -> "reference"

    { Type = system
      DisplayName = link.DisplayName |> Option.defaultValue link.Ref
      ExternalId = Some link.Ref
      Url = link.Url }

/// Builds the follow-up item for a request. Pure and deterministic.
let build options (envelope: Envelope) (request: FollowUpRequest) =
    ownProvenance options envelope request
    |> Result.mapError (fun problem -> { Code = "ValidationFailed"; Problems = [ problem ] })
    |> Result.map (fun block ->
        let clock = Clock.fixedAt envelope.Timestamp
        let creator = ProvenanceActor.toLegacy envelope.Actor
        let created = Item.create clock creator CreatedVia.Integration request.Title

        { created with
            Id = itemIdFor envelope.OperationId
            Kind = FollowUp
            Description = Some request.Reason
            NextAction = nextActionFor request.RequestedAction
            Due = request.DueAt |> Option.map AtInstant
            FollowUp = request.ReviewAfter |> Option.map AtInstant
            Tags = request.Tags
            Sources = request.Source |> Option.map sourceReference |> Option.toList
            Important = request.Priority = "high" || request.Priority = "urgent"
            History =
                [ History.attributedEntry envelope.Timestamp creator (Some envelope.ContributionKey) Created ]
            Provenance = Some(Recorded block)
            ReceivedProvenance =
                match envelope.Provenance with
                | Some(ProvenanceJson.Supported(received, _)) -> Some(Recorded received)
                | Some(ProvenanceJson.Unsupported(schema, json)) -> Some(CarriedVerbatim(schema, json))
                | _ -> None })

/// A replay must match the stored item's own contributions exactly: appending
/// them again must change nothing. Anything else means the operation id was
/// reused for a different request.
let private confirmReplay (fresh: Item) (stored: Item) =
    match fresh.Provenance, stored.Provenance with
    | Some(Recorded freshBlock), Some(Recorded storedBlock) ->
        let unchanged =
            freshBlock.Contributions
            |> List.fold
                (fun acc (key, entry) ->
                    acc
                    |> Result.bind (fun (block, changed) ->
                        Provenance.append key entry block |> Result.map (fun (next, c) -> next, changed || c)))
                (Ok(storedBlock, false))

        match unchanged with
        | Ok(_, false) when fresh.Title = stored.Title -> Ok stored
        | _ ->
            Error
                { Code = "OperationAlreadyProcessed"
                  Problems = [ "this operationId was already used for a different followup.create request" ] }
    | _ ->
        Error
            { Code = "OperationAlreadyProcessed"
              Problems = [ "this operationId was already used for an item this request does not match" ] }

/// Receives `followup.create` (VIG-PROV-009). `lookup` answers whether an item
/// already exists; when it does, the stored item is returned unchanged and no
/// contribution is duplicated (VIG-PROV-011).
let receive options (lookup: ItemId -> Item option) (envelopeJson: string) (payloadJson: string) =
    let envelope = readEnvelope envelopeJson
    let request = readPayload payloadJson

    match envelope, request with
    | Error e1, Error e2 ->
        Error
            { Code = e1.Code
              Problems = e1.Problems @ e2.Problems }
    | Error e, _
    | _, Error e -> Error e
    | Ok envelope, Ok request ->
        let warnings =
            match envelope.Provenance with
            | Some(ProvenanceJson.Supported(_, found)) -> found
            | _ -> []

        build options envelope request
        |> Result.bind (fun fresh ->
            match lookup fresh.Id with
            | Some stored ->
                confirmReplay fresh stored
                |> Result.map (fun item -> { Item = item; Replayed = true; Warnings = warnings })
            | None -> Ok { Item = fresh; Replayed = false; Warnings = warnings })
