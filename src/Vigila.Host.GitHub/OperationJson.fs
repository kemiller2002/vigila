/// Tier 4 - the persisted form of an integration operation record.
///
/// One record per accepted `followup.create` operation, stored at
/// StorageLayout.operationPath. It is the durable half of idempotency: a
/// restarted process, another browser session or another agent reads the same
/// file and so reaches the same answer for the same operation id.
///
/// It is also the lossless copy of the request. Everything the Echelon
/// envelope and contract carried -- provider, run and session ids, branch,
/// correlation id, exact priority, the opaque `context` -- is written here, so
/// nothing is discarded merely because Vigila's Item has no field for it.
///
/// The item is embedded as its own persisted form (ItemJson), so the item's
/// schema stays owned by one module.
///
/// Requirements: VIG-AGT-012, VIG-AGT-013, VIG-PER-020, VIG-PER-021,
/// VIG-PER-022.
module Vigila.Host.GitHub.OperationJson

open System
open System.IO
open System.Text
open System.Text.Json
open Vigila.Semantic.Time
open Vigila.Semantic.Items
open Vigila.Semantic.Tags
open Vigila.Application.Integration

/// The schema version this module writes (VIG-PER-020).
[<Literal>]
let CurrentSchemaVersion = 1

let private actorKindName =
    function
    | AgentKind -> "agent"
    | HumanKind -> "human"
    | AutomationKind -> "automation"
    | SystemKind -> "system"

let private actionName =
    function
    | Review -> "review"
    | Decide -> "decide"
    | Approve -> "approve"
    | ProvideInformation -> "provide-information"
    | Investigate -> "investigate"
    | RequestedAction.Other -> "other"

let private priorityName =
    function
    | Low -> "low"
    | Normal -> "normal"
    | High -> "high"
    | Urgent -> "urgent"

let private parseFrom (cases: ('T -> string)) (all: 'T list) what (text: string) =
    match all |> List.tryFind (fun c -> cases c = text) with
    | Some value -> Ok value
    | None -> Error $"'%s{text}' is not a known %s{what}."

let private writeKnown (w: Utf8JsonWriter) (name: string) value =
    w.WriteStartObject name

    match value with
    | Known v ->
        w.WriteString("state", "known")
        w.WriteString("value", v)
    | Unknown -> w.WriteString("state", "unknown")
    | NotApplicable -> w.WriteString("state", "not-applicable")

    w.WriteEndObject()

let private writeOptionalInstant (w: Utf8JsonWriter) (name: string) =
    function
    | Some instant -> w.WriteString(name, Instant.toIso8601 instant)
    | None -> w.WriteNull name

/// Serialises a record to its persisted form.
let toJson (record: FollowUpRecord) =
    use stream = new MemoryStream()

    (use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
     let envelope = record.Envelope
     let followUp = record.FollowUp

     w.WriteStartObject()
     w.WriteNumber("schemaVersion", CurrentSchemaVersion)
     w.WriteString("capability", Capability)
     w.WriteNumber("contractVersion", ContractVersion)
     w.WriteString("operationId", envelope.OperationId)
     w.WriteString("correlationId", envelope.CorrelationId)
     w.WriteString("requestedAt", Instant.toIso8601 envelope.Timestamp)

     w.WriteStartObject "actor"
     w.WriteString("kind", actorKindName envelope.Actor.Kind)
     writeKnown w "provider" envelope.Actor.Provider
     writeKnown w "identity" envelope.Actor.Identity
     writeKnown w "runId" envelope.Actor.RunId
     writeKnown w "sessionId" envelope.Actor.SessionId
     w.WriteEndObject()

     match envelope.Source with
     | None -> w.WriteNull "source"
     | Some source ->
         w.WriteStartObject "source"
         writeKnown w "repository" source.Repository
         writeKnown w "branch" source.Branch
         writeKnown w "commit" source.Commit
         writeKnown w "workItem" source.WorkItem
         w.WriteEndObject()

     w.WriteStartObject "followUp"
     w.WriteString("title", Title.value followUp.Title)
     w.WriteString("reason", followUp.Reason)
     w.WriteString("requestedAction", actionName followUp.RequestedAction)
     w.WriteString("priority", priorityName followUp.Priority)
     writeOptionalInstant w "reviewAfter" followUp.ReviewAfter
     writeOptionalInstant w "dueAt" followUp.DueAt
     w.WriteStartArray "tags"
     followUp.Tags |> TagSet.toSortedList |> List.iter w.WriteStringValue
     w.WriteEndArray()

     match followUp.Context with
     | Some context -> w.WriteString("context", context)
     | None -> w.WriteNull "context"

     w.WriteEndObject()

     w.WritePropertyName "item"
     w.WriteRawValue(ItemJson.toJson record.Item)
     w.WriteEndObject())

    Encoding.UTF8.GetString(stream.ToArray())

// ---------------------------------------------------------------------------
// Reading.
// ---------------------------------------------------------------------------

let private bind f r = Result.bind f r

let private prop (el: JsonElement) (name: string) =
    match el.TryGetProperty name with
    | true, value when value.ValueKind <> JsonValueKind.Null -> Some value
    | _ -> None

let private requiredString el name =
    match prop el name with
    | Some v when v.ValueKind = JsonValueKind.String ->
        match v.GetString() with
        | Null -> Error $"'%s{name}' is required."
        | NonNull s -> Ok s
    | _ -> Error $"'%s{name}' is required."

let private optionalString el name =
    match prop el name with
    | Some v when v.ValueKind = JsonValueKind.String -> Ok(v.GetString() |> Option.ofObj)
    | Some _ -> Error $"'%s{name}' must be a string."
    | None -> Ok None

let private optionalInstant el name =
    optionalString el name
    |> bind (function
        | None -> Ok None
        | Some text -> Instant.parse text |> Result.map Some)

let private readKnown el name =
    match prop el name with
    | None -> Error $"'%s{name}' is required."
    | Some v ->
        match requiredString v "state", optionalString v "value" with
        | Ok "known", Ok(Some value) -> Ok(Known value)
        | Ok "unknown", Ok _ -> Ok Unknown
        | Ok "not-applicable", Ok _ -> Ok NotApplicable
        | Ok state, Ok _ -> Error $"'%s{name}' has an invalid state '%s{state}'."
        | Error e, _
        | _, Error e -> Error e

let private readActor el =
    match prop el "actor" with
    | None -> Error "'actor' is required."
    | Some a ->
        requiredString a "kind"
        |> bind (parseFrom actorKindName [ AgentKind; HumanKind; AutomationKind; SystemKind ] "actor kind")
        |> bind (fun kind ->
            readKnown a "provider"
            |> bind (fun provider ->
                readKnown a "identity"
                |> bind (fun identity ->
                    readKnown a "runId"
                    |> bind (fun runId ->
                        readKnown a "sessionId"
                        |> Result.map (fun sessionId ->
                            { Kind = kind
                              Provider = provider
                              Identity = identity
                              RunId = runId
                              SessionId = sessionId })))))

let private readSource el =
    match prop el "source" with
    | None -> Ok None
    | Some s ->
        readKnown s "repository"
        |> bind (fun repository ->
            readKnown s "branch"
            |> bind (fun branch ->
                readKnown s "commit"
                |> bind (fun commit ->
                    readKnown s "workItem"
                    |> Result.map (fun workItem ->
                        Some
                            { Repository = repository
                              Branch = branch
                              Commit = commit
                              WorkItem = workItem }))))

let private readTags el =
    match prop el "tags" with
    | None -> Ok TagSet.empty
    | Some arr when arr.ValueKind = JsonValueKind.Array ->
        arr.EnumerateArray()
        |> Seq.map (fun t ->
            if t.ValueKind = JsonValueKind.String then
                t.GetString() |> Option.ofObj |> Option.defaultValue ""
            else
                "")
        |> TagSet.ofStrings
        |> Result.mapError (String.concat " ")
    | Some _ -> Error "'tags' must be an array."

let private readFollowUp el =
    match prop el "followUp" with
    | None -> Error "'followUp' is required."
    | Some f ->
        requiredString f "title"
        |> bind Title.create
        |> bind (fun title ->
            requiredString f "reason"
            |> bind (fun reason ->
                requiredString f "requestedAction"
                |> bind (
                    parseFrom
                        actionName
                        [ Review; Decide; Approve; ProvideInformation; Investigate; RequestedAction.Other ]
                        "requested action"
                )
                |> bind (fun action ->
                    requiredString f "priority"
                    |> bind (parseFrom priorityName [ Low; Normal; High; Urgent ] "priority")
                    |> bind (fun priority ->
                        optionalInstant f "reviewAfter"
                        |> bind (fun reviewAfter ->
                            optionalInstant f "dueAt"
                            |> bind (fun dueAt ->
                                readTags f
                                |> bind (fun tags ->
                                    optionalString f "context"
                                    |> Result.map (fun context ->
                                        { Title = title
                                          Reason = reason
                                          RequestedAction = action
                                          Priority = priority
                                          ReviewAfter = reviewAfter
                                          DueAt = dueAt
                                          Tags = tags
                                          Context = context }))))))))

let private readSchemaVersion el =
    match prop el "schemaVersion" with
    | Some v when v.ValueKind = JsonValueKind.Number ->
        match v.TryGetInt32() with
        | true, CurrentSchemaVersion -> Ok()
        | true, other -> Error $"Operation record schema version %d{other} is not supported."
        | _ -> Error "'schemaVersion' must be an integer."
    | _ -> Error "'schemaVersion' is required."

/// Deserialises a record. Rejects an unknown schema version rather than
/// reading it optimistically (VIG-PER-021), and never repairs a malformed
/// record silently (VIG-PER-030).
let fromJson (json: string) : Result<FollowUpRecord, string> =
    let parsed =
        try
            Ok(JsonDocument.Parse json)
        with :? JsonException as ex ->
            Error $"The operation record is not valid JSON: %s{ex.Message}"

    parsed
    |> bind (fun document ->
        use document = document
        let root = document.RootElement

        readSchemaVersion root
        |> bind (fun () -> requiredString root "operationId")
        |> bind (fun operationId ->
            requiredString root "correlationId"
            |> bind (fun correlationId ->
                requiredString root "requestedAt"
                |> bind Instant.parse
                |> bind (fun requestedAt ->
                    readActor root
                    |> bind (fun actor ->
                        readSource root
                        |> bind (fun source ->
                            readFollowUp root
                            |> bind (fun followUp ->
                                match prop root "item" with
                                | None -> Error "'item' is required."
                                | Some item ->
                                    ItemJson.fromJson (item.GetRawText())
                                    |> Result.map (fun item ->
                                        { Envelope =
                                            { OperationId = operationId
                                              CorrelationId = correlationId
                                              Timestamp = requestedAt
                                              Actor = actor
                                              Source = source }
                                          FollowUp = followUp
                                          Item = item }))))))))
