/// Tier 4 - the persisted form of an item.
///
/// Serialization is a storage concern, so it lives here rather than in the
/// domain: Tier 1 must not know it is ever written down (VIG-GOV-015).
///
/// Two properties drive the shape of this format:
///
///   * Every record carries its schema version, and a reader never assumes the
///     latest (VIG-PER-020, VIG-PER-021).
///   * A date-only value is written as a plain calendar date with no time and
///     no offset, tagged as such. A reader therefore cannot mistake it for a
///     moment and shift it a day (VIG-TIME-016).
///
/// Unknown fields are ignored rather than rejected, so a record written by a
/// newer minor version still loads (VIG-PER-022). Unknown values in a *closed*
/// set - a status, a kind - are still rejected, because guessing there would
/// fabricate domain state.
///
/// Requirements: VIG-PER-020, VIG-PER-021, VIG-PER-022, VIG-TIME-016.
module Vigila.Host.GitHub.ItemJson

open System
open System.Text
open System.Text.Json
open Vigila.Semantic.Identifiers
open Vigila.Semantic.Time
open Vigila.Semantic.Actors
open Vigila.Semantic.Items
open Vigila.Semantic.Tags
open Vigila.Semantic.Notes
open Vigila.Semantic.History
open Vigila.Semantic.Item

/// The schema version this module writes (VIG-PER-020).
[<Literal>]
let CurrentSchemaVersion = 1

/// Date-only values are written in this form, which carries no time and no
/// offset at all.
[<Literal>]
let private DateFormat = "yyyy-MM-dd"

let private actorTypeName =
    function
    | Human -> "human"
    | ActorType.Agent -> "agent"
    | AutomatedProcess -> "automated-process"
    | ActorType.Integration -> "integration"

let private parseActorType =
    function
    | "human" -> Ok Human
    | "agent" -> Ok ActorType.Agent
    | "automated-process" -> Ok AutomatedProcess
    | "integration" -> Ok ActorType.Integration
    | other -> Error $"'%s{other}' is not a known actor type."

let private viaName =
    function
    | CreatedVia.UI -> "ui"
    | CreatedVia.Agent -> "agent"
    | CreatedVia.Integration -> "integration"
    | CreatedVia.Import -> "import"

let private parseVia =
    function
    | "ui" -> Ok CreatedVia.UI
    | "agent" -> Ok CreatedVia.Agent
    | "integration" -> Ok CreatedVia.Integration
    | "import" -> Ok CreatedVia.Import
    | other -> Error $"'%s{other}' is not a known creation channel."

let private kindName =
    function
    | Task -> "task"
    | FollowUp -> "follow-up"
    | ItemKind.Waiting -> "waiting"

let private parseKind =
    function
    | "task" -> Ok Task
    | "follow-up" -> Ok FollowUp
    | "waiting" -> Ok ItemKind.Waiting
    | other -> Error $"'%s{other}' is not a known item kind."

let private statusName =
    function
    | Open -> "open"
    | ItemStatus.Waiting -> "waiting"
    | Deferred -> "deferred"
    | Completed -> "completed"
    | ItemStatus.Cancelled -> "cancelled"

let private parseStatus =
    function
    | "open" -> Ok Open
    | "waiting" -> Ok ItemStatus.Waiting
    | "deferred" -> Ok Deferred
    | "completed" -> Ok Completed
    | "cancelled" -> Ok ItemStatus.Cancelled
    | other -> Error $"'%s{other}' is not a known item status."

// --- writing --------------------------------------------------------------

let private writeActor (w: Utf8JsonWriter) (name: string) (actor: Actor) =
    w.WriteStartObject name
    w.WriteString("type", actorTypeName actor.Type)
    w.WriteString("name", actor.Name)
    w.WriteEndObject()

let private writeInstant (w: Utf8JsonWriter) (name: string) instant =
    w.WriteString(name, Instant.toIso8601 instant)

let private writeOptionalInstant (w: Utf8JsonWriter) (name: string) value =
    match value with
    | Some instant -> writeInstant w name instant
    | None -> w.WriteNull name

/// A when-value is tagged so the reader is told which it is rather than
/// inferring it from the string's shape (VIG-TIME-016).
let private writeWhen (w: Utf8JsonWriter) (name: string) value =
    match value with
    | None -> w.WriteNull name
    | Some(OnDate day) ->
        w.WriteStartObject name
        w.WriteString("kind", "date")
        w.WriteString("value", day.ToString(DateFormat, Globalization.CultureInfo.InvariantCulture))
        w.WriteEndObject()
    | Some(AtInstant instant) ->
        w.WriteStartObject name
        w.WriteString("kind", "instant")
        w.WriteString("value", Instant.toIso8601 instant)
        w.WriteEndObject()

let private writeOptionalString (w: Utf8JsonWriter) (name: string) (value: string option) =
    match value with
    | Some v -> w.WriteString(name, v)
    | None -> w.WriteNull name

let private operationName =
    function
    | Created -> "created"
    | TitleChanged _ -> "title-changed"
    | DescriptionChanged -> "description-changed"
    | StatusChanged _ -> "status-changed"
    | KindChanged _ -> "kind-changed"
    | NextActionChanged _ -> "next-action-changed"
    | DueDateChanged -> "due-date-changed"
    | FollowUpDateChanged -> "follow-up-date-changed"
    | WaitingOnChanged _ -> "waiting-on-changed"
    | Snoozed _ -> "snoozed"
    | SnoozeCleared -> "snooze-cleared"
    | TagAdded _ -> "tag-added"
    | TagRemoved _ -> "tag-removed"
    | NoteAdded -> "note-added"
    | ImportanceChanged _ -> "importance-changed"
    | ReviewFlagChanged _ -> "review-flag-changed"

/// History detail is written as loosely-typed before/after strings.
///
/// Deliberate: history is a human-readable record (VIG-DOM-034a), and pinning
/// its payloads to today's domain types would make every future domain change
/// a migration of historical records that are supposed to be immutable.
let private writeOperationDetail (w: Utf8JsonWriter) operation =
    let pair (before: string option) (after: string option) =
        writeOptionalString w "before" before
        writeOptionalString w "after" after

    match operation with
    | TitleChanged(b, a) -> pair (Some b) (Some a)
    | StatusChanged(b, a) -> pair (Some(statusName b)) (Some(statusName a))
    | KindChanged(b, a) -> pair (Some(kindName b)) (Some(kindName a))
    | NextActionChanged(b, a) -> pair b a
    | WaitingOnChanged(b, a) -> pair b a
    | TagAdded tag
    | TagRemoved tag -> w.WriteString("tag", Tag.value tag)
    | Snoozed until -> w.WriteString("until", Instant.toIso8601 until)
    | ImportanceChanged v -> w.WriteBoolean("important", v)
    | ReviewFlagChanged v -> w.WriteBoolean("needsReview", v)
    | Created
    | DescriptionChanged
    | DueDateChanged
    | FollowUpDateChanged
    | SnoozeCleared
    | NoteAdded -> ()

let private resolutionName =
    function
    | Superseded -> "superseded"
    | PromotedToRos -> "promoted-to-ros"
    | NoLongerRelevant -> "no-longer-relevant"
    | Other -> "other"

/// Rejects "cancelled" and "completed-successfully" rather than mapping them:
/// they are no longer classifications (OQ-09), and silently accepting them
/// would let a value the domain cannot represent back in.
let private parseResolution =
    function
    | "superseded" -> Ok Superseded
    | "promoted-to-ros" -> Ok PromotedToRos
    | "no-longer-relevant" -> Ok NoLongerRelevant
    | "other" -> Ok Other
    | other -> Error $"'%s{other}' is not a known resolution."

/// Serialises an item to its persisted form.
let toJson (item: Item) =
    use stream = new IO.MemoryStream()

    (use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

     writer.WriteStartObject()
     writer.WriteNumber("schemaVersion", CurrentSchemaVersion)
     writer.WriteString("id", (ItemId.toGuid item.Id).ToString("D"))
     writer.WriteString("title", Title.value item.Title)
     writer.WriteString("kind", kindName item.Kind)
     writer.WriteString("status", statusName item.Status)

     writeOptionalString writer "description" item.Description
     writeOptionalString writer "nextAction" item.NextAction
     writeWhen writer "due" item.Due
     writeWhen writer "followUp" item.FollowUp
     writeOptionalInstant writer "snoozedUntil" item.SnoozedUntil
     writeOptionalString writer "waitingOn" item.WaitingOn
     writeOptionalInstant writer "waitingSince" item.WaitingSince

     writer.WriteStartArray "tags"
     for tag in TagSet.toSortedList item.Tags do
         writer.WriteStringValue tag
     writer.WriteEndArray()

     writer.WriteStartArray "notes"
     for note in item.Notes do
         writer.WriteStartObject()
         writer.WriteString("id", (NoteId.toGuid note.Id).ToString("D"))
         writer.WriteString("text", note.Text)
         writeInstant writer "createdAt" note.CreatedAt
         writeActor writer "createdBy" note.CreatedBy
         writer.WriteEndObject()
     writer.WriteEndArray()

     writer.WriteStartArray "sources"
     for source in item.Sources do
         writer.WriteStartObject()
         writer.WriteString("type", source.Type)
         writer.WriteString("displayName", source.DisplayName)
         writeOptionalString writer "externalId" source.ExternalId
         writeOptionalString writer "url" source.Url
         writer.WriteEndObject()
     writer.WriteEndArray()

     writer.WriteBoolean("important", item.Important)
     writer.WriteBoolean("needsReview", item.NeedsReview)
     writeOptionalString writer "resolution" (item.Resolution |> Option.map resolutionName)
     writeOptionalString writer "resolutionNote" item.ResolutionNote

     writeInstant writer "createdAt" item.CreatedAt
     writeActor writer "createdBy" item.CreatedBy
     writer.WriteString("createdVia", viaName item.CreatedVia)
     writeInstant writer "updatedAt" item.UpdatedAt
     writeOptionalInstant writer "completedAt" item.CompletedAt
     writeOptionalInstant writer "cancelledAt" item.CancelledAt
     writeInstant writer "lastActivityAt" item.LastActivityAt

     writer.WriteStartArray "history"
     for e in item.History do
         writer.WriteStartObject()
         writeInstant writer "at" e.At
         writeActor writer "actor" e.Actor
         writer.WriteString("operation", operationName e.Operation)
         writeOperationDetail writer e.Operation
         writer.WriteEndObject()
     writer.WriteEndArray()

     writer.WriteEndObject())

    Encoding.UTF8.GetString(stream.ToArray())

// --- reading --------------------------------------------------------------

/// Minimal Result computation expression.
///
/// The reader parses ~20 fields, each of which can fail. Without this the
/// function is a staircase of nested matches, and a staircase is where a
/// forgotten error case hides.
type private ResultBuilder() =
    member _.Bind(v, f) = Result.bind f v
    member _.Return v = Ok v
    member _.ReturnFrom(v: Result<_, _>) = v

let private result = ResultBuilder()

let private prop (el: JsonElement) (name: string) =
    match el.TryGetProperty name with
    | true, v when v.ValueKind <> JsonValueKind.Null -> Some v
    | _ -> None

/// `JsonElement.GetString()` is typed as nullable, so the null case is handled
/// here rather than at each of the twenty-odd call sites. A JSON null already
/// failed `prop`, so reaching null here means the document is malformed.
let private requiredString el name =
    match prop el name with
    | Some v when v.ValueKind = JsonValueKind.String ->
        match v.GetString() with
        | NonNull text -> Ok text
        | Null -> Error $"'%s{name}' must be a string."
    | Some _ -> Error $"'%s{name}' must be a string."
    | None -> Error $"'%s{name}' is required."

let private optionalString el name =
    match prop el name with
    | Some v when v.ValueKind = JsonValueKind.String ->
        match v.GetString() with
        | NonNull text -> Ok(Some text)
        | Null -> Error $"'%s{name}' must be a string."
    | Some _ -> Error $"'%s{name}' must be a string."
    | None -> Ok None

let private optionalBool el name =
    match prop el name with
    | Some v when v.ValueKind = JsonValueKind.True || v.ValueKind = JsonValueKind.False -> Ok(v.GetBoolean())
    | Some _ -> Error $"'%s{name}' must be a boolean."
    | None -> Ok false

let private requiredInstant el name =
    requiredString el name |> Result.bind Instant.parse

let private optionalInstant el name =
    match prop el name with
    | None -> Ok None
    | Some _ -> requiredInstant el name |> Result.map Some

let private requiredGuid el name =
    result {
        let! text = requiredString el name

        return!
            match Guid.TryParse text with
            | true, g -> Ok g
            | _ -> Error $"'%s{name}' is not a valid identifier."
    }

let private readActor el name =
    match prop el name with
    | None -> Error $"'%s{name}' is required."
    | Some a ->
        result {
            let! typeName = requiredString a "type"
            let! actorType = parseActorType typeName
            let! actorName = requiredString a "name"
            return! Actor.create actorType actorName
        }

/// Reads a tagged when-value.
///
/// The `kind` discriminator is required rather than inferred from the string's
/// shape: inferring is exactly how a date-only value becomes a midnight
/// timestamp and then shifts a day (VIG-TIME-016).
let private readWhen el name =
    match prop el name with
    | None -> Ok None
    | Some w ->
        result {
            let! kind = requiredString w "kind"
            let! value = requiredString w "value"

            return!
                match kind with
                | "date" ->
                    match DateOnly.TryParseExact(value, DateFormat, Globalization.CultureInfo.InvariantCulture,
                                                 Globalization.DateTimeStyles.None) with
                    | true, d -> Ok(Some(OnDate d))
                    | _ -> Error $"'%s{value}' is not a valid date."
                | "instant" -> Instant.parse value |> Result.map (AtInstant >> Some)
                | other -> Error $"'%s{other}' is not a known date kind."
        }

let private readAll f (el: JsonElement) name =
    match prop el name with
    | None -> Ok []
    | Some arr when arr.ValueKind = JsonValueKind.Array ->
        arr.EnumerateArray()
        |> Seq.fold
            (fun acc e ->
                match acc, f e with
                | Error msg, _ -> Error msg
                | Ok items, Ok item -> Ok(item :: items)
                | Ok _, Error msg -> Error msg)
            (Ok [])
        |> Result.map List.rev
    | Some _ -> Error $"'%s{name}' must be an array."

let private readNote itemId (e: JsonElement) =
    result {
        let! id = requiredGuid e "id"
        let! text = requiredString e "text"
        let! createdAt = requiredInstant e "createdAt"
        let! createdBy = readActor e "createdBy"

        return
            { Id = NoteId.ofGuid id
              ItemId = itemId
              Text = text
              CreatedAt = createdAt
              CreatedBy = createdBy }
    }

let private readSource (e: JsonElement) =
    result {
        let! sourceType = requiredString e "type"
        let! displayName = requiredString e "displayName"
        let! externalId = optionalString e "externalId"
        let! url = optionalString e "url"

        return
            { Type = sourceType
              DisplayName = displayName
              ExternalId = externalId
              Url = url }
    }

/// Reads a history entry.
///
/// Only `at`, `actor` and the operation name are reconstructed into domain
/// values; the before/after payloads stay in the persisted record. History is
/// an immutable account of what happened, and re-typing old payloads against
/// today's domain would make every domain change rewrite the past.
let private readHistory (e: JsonElement) =
    result {
        let! at = requiredInstant e "at"
        let! actor = readActor e "actor"
        let! name = requiredString e "operation"

        let! operation =
            match name with
            | "created" -> Ok Created
            | "description-changed" -> Ok DescriptionChanged
            | "due-date-changed" -> Ok DueDateChanged
            | "follow-up-date-changed" -> Ok FollowUpDateChanged
            | "snooze-cleared" -> Ok SnoozeCleared
            | "note-added" -> Ok NoteAdded
            | "title-changed" ->
                result {
                    let! b = optionalString e "before"
                    let! a = optionalString e "after"
                    return TitleChanged(defaultArg b "", defaultArg a "")
                }
            | "status-changed" ->
                result {
                    let! b = requiredString e "before"
                    let! a = requiredString e "after"
                    let! before = parseStatus b
                    let! after = parseStatus a
                    return StatusChanged(before, after)
                }
            | "kind-changed" ->
                result {
                    let! b = requiredString e "before"
                    let! a = requiredString e "after"
                    let! before = parseKind b
                    let! after = parseKind a
                    return KindChanged(before, after)
                }
            | "next-action-changed" ->
                result {
                    let! b = optionalString e "before"
                    let! a = optionalString e "after"
                    return NextActionChanged(b, a)
                }
            | "waiting-on-changed" ->
                result {
                    let! b = optionalString e "before"
                    let! a = optionalString e "after"
                    return WaitingOnChanged(b, a)
                }
            | "tag-added" -> requiredString e "tag" |> Result.bind Tag.create |> Result.map TagAdded
            | "tag-removed" -> requiredString e "tag" |> Result.bind Tag.create |> Result.map TagRemoved
            | "snoozed" -> requiredInstant e "until" |> Result.map Snoozed
            | "importance-changed" -> optionalBool e "important" |> Result.map ImportanceChanged
            | "review-flag-changed" -> optionalBool e "needsReview" |> Result.map ReviewFlagChanged
            | other -> Error $"'%s{other}' is not a known history operation."

        return { At = at; Actor = actor; Operation = operation }
    }

/// Deserialises an item from its persisted form.
///
/// Rejects a record whose schema version this build does not understand rather
/// than reading it optimistically (VIG-PER-014, VIG-PER-022). Ignores fields it
/// does not recognise, so a record written by a newer *compatible* version
/// still loads.
let fromJson (json: string) =
    let parsed =
        try
            Ok(JsonDocument.Parse json)
        with :? JsonException as ex ->
            Error $"The record is not valid JSON: %s{ex.Message}"

    match parsed with
    | Error msg -> Error msg
    | Ok document ->
        use document = document
        let el = document.RootElement

        result {
            let! version =
                match prop el "schemaVersion" with
                | Some v when v.ValueKind = JsonValueKind.Number -> Ok(v.GetInt32())
                | Some _ -> Error "'schemaVersion' must be a number."
                | None -> Error "'schemaVersion' is required."

            do!
                if version > CurrentSchemaVersion then
                    Error
                        $"The record uses schema version %d{version}, but this build understands at most %d{CurrentSchemaVersion}."
                elif version < 1 then
                    Error $"Schema version %d{version} is not valid."
                else
                    Ok()

            let! idGuid = requiredGuid el "id"
            let id = ItemId.ofGuid idGuid

            let! titleText = requiredString el "title"
            let! title = Title.create titleText
            let! kindName' = requiredString el "kind"
            let! kind = parseKind kindName'
            let! statusName' = requiredString el "status"
            let! status = parseStatus statusName'

            let! description = optionalString el "description"
            let! nextAction = optionalString el "nextAction"
            let! due = readWhen el "due"
            let! followUp = readWhen el "followUp"
            let! snoozedUntil = optionalInstant el "snoozedUntil"
            let! waitingOn = optionalString el "waitingOn"
            let! waitingSince = optionalInstant el "waitingSince"

            let! tagList =
                readAll
                    (fun (e: JsonElement) ->
                        if e.ValueKind = JsonValueKind.String then
                            Tag.create (e.GetString())
                        else
                            Error "A tag must be a string.")
                    el
                    "tags"

            let! notes = readAll (readNote id) el "notes"
            let! sources = readAll readSource el "sources"
            let! important = optionalBool el "important"
            let! needsReview = optionalBool el "needsReview"

            let! resolutionText = optionalString el "resolution"

            let! resolution =
                match resolutionText with
                | None -> Ok None
                | Some t -> parseResolution t |> Result.map Some

            let! resolutionNote = optionalString el "resolutionNote"

            let! createdAt = requiredInstant el "createdAt"
            let! createdBy = readActor el "createdBy"
            let! viaText = requiredString el "createdVia"
            let! via = parseVia viaText
            let! updatedAt = requiredInstant el "updatedAt"
            let! completedAt = optionalInstant el "completedAt"
            let! cancelledAt = optionalInstant el "cancelledAt"
            let! lastActivityAt = requiredInstant el "lastActivityAt"
            let! history = readAll readHistory el "history"

            return
                { Id = id
                  Title = title
                  Kind = kind
                  Status = status
                  Description = description
                  NextAction = nextAction
                  Due = due
                  FollowUp = followUp
                  SnoozedUntil = snoozedUntil
                  WaitingOn = waitingOn
                  WaitingSince = waitingSince
                  Tags = Set.ofList tagList
                  Notes = notes
                  Sources = sources
                  Important = important
                  NeedsReview = needsReview
                  Resolution = resolution
                  ResolutionNote = resolutionNote
                  CreatedAt = createdAt
                  CreatedBy = createdBy
                  CreatedVia = via
                  UpdatedAt = updatedAt
                  CompletedAt = completedAt
                  CancelledAt = cancelledAt
                  LastActivityAt = lastActivityAt
                  History = history }
        }
