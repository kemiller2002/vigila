/// Tier 3 - the wire form of the `followup.create` boundary.
///
/// Decodes a JSON invocation into a CreateRequest and encodes a CreateOutcome
/// as a receipt. This is where contract-version negotiation, timestamp parsing
/// and the contracts' `additionalProperties: false` are enforced, so the typed
/// boundary behind it only ever sees well-formed input.
///
/// The invocation document is Vigila's own; its `envelope` and `payload`
/// members are exactly echelon-registry's `echelon.execution-envelope/v1` and
/// `followup.create` v1:
///
///     { "capability": "followup.create",
///       "contractVersion": 1,
///       "envelope": { ... },
///       "payload": { ... } }
///
/// Unknown members are refused rather than ignored, so no contract field is
/// silently discarded. Nothing here throws on bad input: malformed JSON is a
/// refusal like any other.
///
/// Requirements: VIG-AGT-014, VIG-AGT-015, VIG-AGT-035, VIG-AGT-050.
module Vigila.Application.IntegrationWire

open System
open System.Globalization
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open Aegis
open Vigila.Semantic.Identifiers
open Vigila.Semantic.Items
open Vigila.Semantic.Time
open Vigila.Application.Integration

let private refusal code field message =
    { Code = code
      Field = field
      Message = message }

let private invalid field message = [ refusal ValidationFailed field message ]

let private unsupported field message = [ refusal SchemaUnsupported field message ]

/// Collects every failure across independent fields.
let private combine (results: Result<'T, Refusal list> list) =
    results
    |> List.fold
        (fun acc next ->
            match acc, next with
            | Ok values, Ok value -> Ok(values @ [ value ])
            | Ok _, Error e -> Error e
            | Error e, Ok _ -> Error e
            | Error e1, Error e2 -> Error(e1 @ e2))
        (Ok [])

let private property (el: JsonElement) (name: string) =
    match el.TryGetProperty name with
    | true, value when value.ValueKind <> JsonValueKind.Null -> Some value
    | _ -> None

let private requireObject field (el: JsonElement) =
    if el.ValueKind = JsonValueKind.Object then Ok el
    else Error(invalid field $"%s{field} must be an object.")

/// Enforces `additionalProperties: false`.
let private onlyKnown field (allowed: string list) (el: JsonElement) =
    let unknown =
        el.EnumerateObject()
        |> Seq.map (fun p -> p.Name)
        |> Seq.filter (fun name -> not (List.contains name allowed))
        |> Seq.toList

    match unknown with
    | [] -> Ok()
    | names ->
        Error(
            names
            |> List.collect (fun name -> invalid $"%s{field}.%s{name}" $"'%s{name}' is not a %s{field} field.")
        )

let private stringAt field el name =
    match property el name with
    | None -> Ok None
    | Some value when value.ValueKind = JsonValueKind.String -> Ok(value.GetString() |> Option.ofObj)
    | Some _ -> Error(invalid field $"%s{field} must be a string.")

let private requiredString field el name =
    match stringAt field el name with
    | Ok(Some value) -> Ok value
    | Ok None -> Error(invalid field $"%s{field} is required.")
    | Error e -> Error e

/// RFC 3339 `date-time` requires an explicit offset. A timestamp without one
/// names no single moment, so it is refused rather than read as local time
/// (VIG-TIME-015).
let private explicitOffset = Regex(@"(Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase)

let private instantAt field el name =
    match stringAt field el name with
    | Error e -> Error e
    | Ok None -> Ok None
    | Ok(Some text) ->
        match DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None) with
        | true, value when text.Contains 'T' && explicitOffset.IsMatch text ->
            Ok(Some(Instant.ofDateTimeOffset value))
        | _ -> Error(invalid field $"'%s{text}' is not an RFC 3339 date-time with an explicit offset.")

let private enumAt field el name (cases: (string * 'T) list) =
    let names = cases |> List.map fst |> String.concat ", "

    match requiredString field el name with
    | Error e -> Error e
    | Ok text ->
        match cases |> List.tryFind (fun (n, _) -> n = text) with
        | Some(_, value) -> Ok value
        | None -> Error(invalid field $"'%s{text}' is not one of: %s{names}.")

let private knownValueAt field el name =
    match property el name with
    | None -> Ok Unknown
    | Some value ->
        match requireObject field value with
        | Error e -> Error e
        | Ok obj ->
            match onlyKnown field [ "state"; "value" ] obj, stringAt $"%s{field}.value" obj "value" with
            | Error e, _
            | _, Error e -> Error e
            | Ok(), Ok text ->
                match requiredString $"%s{field}.state" obj "state", text with
                | Ok "known", Some v -> Ok(Known v)
                | Ok "known", None -> Error(invalid field $"%s{field} is marked known but has no value.")
                | Ok "unknown", _ -> Ok Unknown
                | Ok "not-applicable", _ -> Ok NotApplicable
                | Ok other, _ -> Error(invalid $"%s{field}.state" $"'%s{other}' is not one of: known, unknown, not-applicable.")
                | Error e, _ -> Error e

let private actorAt (el: JsonElement) =
    match property el "actor" with
    | None -> Error(invalid "envelope.actor" "envelope.actor is required.")
    | Some value ->
        match requireObject "envelope.actor" value with
        | Error e -> Error e
        | Ok actor ->
            let kind =
                enumAt
                    "envelope.actor.kind"
                    actor
                    "kind"
                    [ "agent", AgentKind
                      "human", HumanKind
                      "automation", AutomationKind
                      "system", SystemKind ]

            let presence name =
                match property actor name with
                | Some _ -> Ok()
                | None -> Error(invalid $"envelope.actor.%s{name}" $"envelope.actor.%s{name} is required.")

            match
                onlyKnown "envelope.actor" [ "kind"; "provider"; "identity"; "runId"; "sessionId" ] actor,
                combine [ presence "provider"; presence "identity" ],
                kind,
                combine
                    [ knownValueAt "envelope.actor.provider" actor "provider"
                      knownValueAt "envelope.actor.identity" actor "identity"
                      knownValueAt "envelope.actor.runId" actor "runId"
                      knownValueAt "envelope.actor.sessionId" actor "sessionId" ]
            with
            | Ok(), Ok _, Ok kind, Ok [ provider; identity; runId; sessionId ] ->
                Ok
                    { Kind = kind
                      Provider = provider
                      Identity = identity
                      RunId = runId
                      SessionId = sessionId }
            | a, b, c, d ->
                let errs r = match r with Error e -> e | Ok _ -> []
                Error(errs a @ errs b @ errs c @ errs d)

let private sourceAt (el: JsonElement) =
    match property el "source" with
    | None -> Ok None
    | Some value ->
        match requireObject "envelope.source" value with
        | Error e -> Error e
        | Ok source ->
            match
                onlyKnown "envelope.source" [ "repository"; "branch"; "commit"; "workItem" ] source,
                combine
                    [ knownValueAt "envelope.source.repository" source "repository"
                      knownValueAt "envelope.source.branch" source "branch"
                      knownValueAt "envelope.source.commit" source "commit"
                      knownValueAt "envelope.source.workItem" source "workItem" ]
            with
            | Ok(), Ok [ repository; branch; commit; workItem ] ->
                Ok(
                    Some
                        { Repository = repository
                          Branch = branch
                          Commit = commit
                          WorkItem = workItem }
                )
            | a, b ->
                let errs r = match r with Error e -> e | Ok _ -> []
                Error(errs a @ errs b)

let private envelopeAt (root: JsonElement) =
    match property root "envelope" with
    | None -> Error(invalid "envelope" "envelope is required.")
    | Some value ->
        match requireObject "envelope" value with
        | Error e -> Error e
        | Ok env ->
            let schema =
                match requiredString "envelope.schema" env "schema" with
                | Ok EnvelopeSchema -> Ok()
                | Ok other ->
                    Error(unsupported "envelope.schema" $"Envelope schema '%s{other}' is not supported; expected '%s{EnvelopeSchema}'.")
                | Error e -> Error e

            let timestamp =
                match instantAt "envelope.timestamp" env "timestamp" with
                | Ok(Some t) -> Ok t
                | Ok None -> Error(invalid "envelope.timestamp" "envelope.timestamp is required.")
                | Error e -> Error e

            let operationId = stringAt "envelope.operationId" env "operationId"
            let correlationId = stringAt "envelope.correlationId" env "correlationId"

            let known =
                onlyKnown "envelope" [ "schema"; "operationId"; "correlationId"; "timestamp"; "actor"; "source" ] env

            match known, schema, timestamp, operationId, correlationId, actorAt env, sourceAt env with
            | Ok(), Ok(), Ok timestamp, Ok operationId, Ok correlationId, Ok actor, Ok source ->
                // Blank or missing ids are left for Integration.validate, which
                // owns that rule for typed callers too.
                Ok
                    { OperationId = operationId |> Option.defaultValue ""
                      CorrelationId = correlationId |> Option.defaultValue ""
                      Timestamp = timestamp
                      Actor = actor
                      Source = source }
            | a, b, c, d, e, f, g ->
                let errs r = match r with Error e -> e | Ok _ -> []
                Error(errs a @ errs b @ errs c @ errs d @ errs e @ errs f @ errs g)

let private tagsAt (el: JsonElement) =
    match property el "tags" with
    | None -> Ok []
    | Some value when value.ValueKind = JsonValueKind.Array ->
        value.EnumerateArray()
        |> Seq.toList
        |> List.mapi (fun i tag ->
            if tag.ValueKind = JsonValueKind.String then
                Ok(tag.GetString() |> Option.ofObj |> Option.defaultValue "")
            else
                Error(invalid $"payload.tags[%d{i}]" "A tag must be a string."))
        |> combine
    | Some _ -> Error(invalid "payload.tags" "payload.tags must be an array.")

let private contextAt (el: JsonElement) =
    match property el "context" with
    | None -> Ok None
    | Some value when value.ValueKind = JsonValueKind.Object -> Ok(Some(value.GetRawText()))
    | Some _ -> Error(invalid "payload.context" "payload.context must be an object or null.")

let private payloadAt (root: JsonElement) : Result<FollowUpCreate, Refusal list> =
    match property root "payload" with
    | None -> Error(invalid "payload" "payload is required.")
    | Some value ->
        match requireObject "payload" value with
        | Error e -> Error e
        | Ok p ->
            let known =
                onlyKnown
                    "payload"
                    [ "title"; "reason"; "requestedAction"; "priority"; "reviewAfter"; "dueAt"; "tags"; "context" ]
                    p

            let action =
                enumAt
                    "payload.requestedAction"
                    p
                    "requestedAction"
                    [ "review", Review
                      "decide", Decide
                      "approve", Approve
                      "provide-information", ProvideInformation
                      "investigate", Investigate
                      "other", RequestedAction.Other ]

            let priority =
                enumAt "payload.priority" p "priority" [ "low", Low; "normal", Normal; "high", High; "urgent", Urgent ]

            match
                known,
                stringAt "payload.title" p "title",
                stringAt "payload.reason" p "reason",
                action,
                priority,
                instantAt "payload.reviewAfter" p "reviewAfter",
                instantAt "payload.dueAt" p "dueAt",
                tagsAt p,
                contextAt p
            with
            | Ok(), Ok title, Ok reason, Ok action, Ok priority, Ok reviewAfter, Ok dueAt, Ok tags, Ok context ->
                // Blank or missing title and reason are left for
                // Integration.validate, which owns those rules.
                Ok
                    { FollowUpCreate.Title = title |> Option.defaultValue ""
                      Reason = reason |> Option.defaultValue ""
                      RequestedAction = action
                      Priority = priority
                      ReviewAfter = reviewAfter
                      DueAt = dueAt
                      Tags = tags
                      Context = context }
            | a, b, c, d, e, f, g, h, i ->
                let errs r = match r with Error e -> e | Ok _ -> []
                Error(errs a @ errs b @ errs c @ errs d @ errs e @ errs f @ errs g @ errs h @ errs i)

let private negotiate (root: JsonElement) =
    let capability =
        match requiredString "capability" root "capability" with
        | Ok Capability -> Ok()
        | Ok other -> Error(unsupported "capability" $"Capability '%s{other}' is not provided by this boundary; expected '%s{Capability}'.")
        | Error e -> Error e

    let version =
        match property root "contractVersion" with
        | Some v when v.ValueKind = JsonValueKind.Number ->
            match v.TryGetInt32() with
            | true, ContractVersion -> Ok()
            | _ ->
                Error(
                    unsupported
                        "contractVersion"
                        $"Contract version %s{v.GetRawText()} is not supported; this boundary implements %s{Capability} v%d{ContractVersion}."
                )
        | Some _ -> Error(invalid "contractVersion" "contractVersion must be an integer.")
        | None -> Error(invalid "contractVersion" "contractVersion is required.")

    match capability, version with
    | Ok(), Ok() -> Ok()
    | a, b ->
        let errs r = match r with Error e -> e | Ok _ -> []
        Error(errs a @ errs b)

/// Decodes an invocation. A version or capability mismatch is reported alone,
/// because the rest of the document cannot be interpreted against a contract
/// this boundary does not implement.
let decode (json: string) : Result<CreateRequest, Refusal list> =
    let parsed =
        try
            Ok(JsonDocument.Parse json)
        with :? JsonException as ex ->
            Error(invalid "$" $"The request is not valid JSON: %s{ex.Message}")

    match parsed with
    | Error e -> Error e
    | Ok document ->
        use document = document
        let root = document.RootElement

        match requireObject "$" root with
        | Error e -> Error e
        | Ok root ->
            match negotiate root with
            | Error e -> Error e
            | Ok() ->
                match
                    onlyKnown "invocation" [ "capability"; "contractVersion"; "envelope"; "payload" ] root,
                    envelopeAt root,
                    payloadAt root
                with
                | Ok(), Ok envelope, Ok payload -> Ok { Envelope = envelope; FollowUp = payload }
                | a, b, c ->
                    let errs r = match r with Error e -> e | Ok _ -> []
                    Error(errs a @ errs b @ errs c)

// ---------------------------------------------------------------------------
// Receipts (VIG-AGT-014). Safe to log: no token, no storage path, no
// transport detail (VIG-AGT-013, VIG-AGT-015).
// ---------------------------------------------------------------------------

let private refusalCodeName =
    function
    | ValidationFailed -> "ValidationFailed"
    | SchemaUnsupported -> "SchemaUnsupported"

let private writeRecord (w: Utf8JsonWriter) (record: FollowUpRecord) =
    w.WriteString("operationId", record.Envelope.OperationId)
    w.WriteString("correlationId", record.Envelope.CorrelationId)
    w.WriteString("itemId", (ItemId.toGuid record.Item.Id).ToString("D"))
    w.WriteString("itemDisplayId", ItemId.display record.Item.Id)
    w.WriteString("createdAt", Instant.toIso8601 record.Item.CreatedAt)

/// Encodes an outcome as a receipt.
let encode (outcome: CreateOutcome) =
    use stream = new MemoryStream()

    (use w = new Utf8JsonWriter(stream)
     w.WriteStartObject()
     w.WriteString("capability", Capability)
     w.WriteNumber("contractVersion", ContractVersion)
     w.WriteString("status", (if CreateOutcome.isSuccess outcome then "success" else "failure"))
     w.WriteString("code", CreateOutcome.code outcome)

     match outcome with
     | Created record
     | Replayed record
     | Conflicted record -> writeRecord w record
     | Rejected refusals ->
         w.WriteStartArray("errors")

         refusals
         |> List.iter (fun r ->
             w.WriteStartObject()
             w.WriteString("code", refusalCodeName r.Code)
             w.WriteString("field", r.Field)
             w.WriteString("message", r.Message)
             w.WriteEndObject())

         w.WriteEndArray()
     | Failed fault ->
         w.WriteString("reference", Presentation.reference fault)
         w.WriteString("message", fault.UserMessage)
         w.WriteBoolean("retrySafe", true)

     w.WriteEndObject())

    Encoding.UTF8.GetString(stream.ToArray())

/// Decode, create, encode. A total function from request text to receipt text.
let invoke aegis clock ledger (json: string) =
    match decode json with
    | Error refusals -> encode (Rejected refusals)
    | Ok request -> encode (Integration.create aegis clock ledger request)
