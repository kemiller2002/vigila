/// Tier 3 - the Limen engine.
///
/// Limen splits a browser application into *capability* (the browser, which can
/// touch the DOM, the network and storage) and *authority* (the engine, which
/// decides what is true and what may happen). This module is Vigila's authority
/// side: it receives a `BrowserToEngineMessage` as JSON and returns an
/// `EngineToBrowserMessage` as JSON.
///
/// Two consequences follow from that contract, and both are requirements Vigila
/// already had:
///
///   * The engine never calls `fetch` or touches `localStorage`. It *asks* for
///     an effect and the kernel performs it (VIG-GOV-014).
///   * Nothing but plain serializable data crosses, so the domain cannot leak a
///     browser type into itself (VIG-GOV-015).
///
/// The protocol is Limen's, version 1. See the `protocol` export of
/// `@echelon-foundry/typescript-wasm-kernel`.
module Vigila.Application.Dispatch

open System
open System.Text
open System.Text.Json
open Vigila.Semantic.Identifiers
open Vigila.Semantic.Time
open Vigila.Semantic.Actors
open Vigila.Semantic.Items
open Vigila.Semantic.Item

/// The engine's authoritative state.
///
/// One case for now. Capture needs no loading state because nothing is fetched
/// yet; persistence adds `Submitting` and the states around it when the GitHub
/// effects land.
[<NoComparison>]
type State =
    { Items: Item list
      Draft: string
      Error: string }

let initial =
    { Items = []
      Draft = ""
      Error = "" }

/// The clock the engine hands to the domain.
///
/// Reading the wall clock is an effect, and VIG-TIME-023 keeps it out of domain
/// logic — so it is read here, at the composition root, and passed in. Limen's
/// protocol has no Time effect to route it through; if one is added later this
/// is the single place that changes.
let private clock =
    Clock.create (fun () -> Instant.ofDateTimeOffset DateTimeOffset.UtcNow)

/// Who the engine attributes changes to.
///
/// A placeholder until the connection flow identifies the user
/// (VIG-SEC-001). It is deliberately not "unknown": VIG-DOM-035 wants an actor
/// type and a name, and "the person at this browser" is what this is.
let private localUser =
    match Actor.human "local" with
    | Ok actor -> actor
    | Error message -> failwith $"The local actor is invalid: %s{message}"

// ---------------------------------------------------------------------------
// Commands - what the engine may be asked to do
// ---------------------------------------------------------------------------

type Command =
    | EditDraft of string
    | Capture
    | Ignored

/// Maps a semantic event to a command.
///
/// Unknown event names become `Ignored` rather than an error: the DOM is not
/// authoritative, and an event the engine does not understand is a binding
/// mistake, not a domain failure.
let eventToCommand (name: string) (value: string option) =
    match name with
    | "titleChanged" -> EditDraft(defaultArg value "")
    | "capture" -> Capture
    | _ -> Ignored

// ---------------------------------------------------------------------------
// Transitions - pure, and the only place these decisions are made
// ---------------------------------------------------------------------------

/// Applies a command.
///
/// `Capture` defers the one rule it needs to `Title.create`, which is Tier 1.
/// The engine does not re-implement "a title is required" — if it did, the same
/// rule would exist twice and could disagree with itself.
let apply command state =
    match command with
    | Ignored -> state
    | EditDraft value -> { state with Draft = value; Error = "" }
    | Capture ->
        match Title.create state.Draft with
        | Error message -> { state with Error = message }
        | Ok title ->
            let item = Item.create clock localUser CreatedVia.UI title

            { state with
                Items = item :: state.Items
                Draft = ""
                Error = "" }

// ---------------------------------------------------------------------------
// Projection - what the browser is allowed to see
// ---------------------------------------------------------------------------

/// Whether capture would currently be refused.
///
/// Projected explicitly rather than left for the DOM to infer from the draft,
/// because "is this legal" is an engine decision (VIG-GOV-013).
let captureDisabled state = Title.create state.Draft |> Result.isError

/// The name the browser sees for a kind.
///
/// Kept separate from the case names so renaming a case cannot silently change
/// the wire contract — the same rule `GitHubStore.codeOf` follows.
let private kindName =
    function
    | Task -> "Task"
    | FollowUp -> "Follow-up"
    | ItemKind.Waiting -> "Waiting"

let private statusName =
    function
    | Open -> "Open"
    | ItemStatus.Waiting -> "Waiting"
    | Deferred -> "Deferred"
    | Completed -> "Completed"
    | ItemStatus.Cancelled -> "Cancelled"

/// What one view key may hold.
///
/// Limen's `ViewState` admits primitives and flat item arrays only. That is
/// modelled here as a closed set rather than left for a serializer to infer
/// from whatever shape the projection happened to have: the wire contract is
/// decided in this file, by these types, and the compiler checks that every
/// case is rendered.
type ViewPrimitive =
    | Text of string
    | Flag of bool
    | Count of int

type ViewValue =
    | Value of ViewPrimitive
    | Rows of (string * ViewPrimitive) list list

/// The fields each row of `items` carries.
let private itemRow item =
    [ "id", Text(ItemId.display item.Id)
      "title", Text(Title.value item.Title)
      "kind", Text(kindName item.Kind)
      "status", Text(statusName item.Status) ]

/// Builds the view.
///
/// Deliberately shallow: notes, history and dates stay in the engine until a
/// view needs them. The keys here are the whole vocabulary the HTML may bind
/// to, and `Vigila.Application.Tests` asserts the two agree.
let project state : (string * ViewValue) list =
    [ "draft", Value(Text state.Draft)
      "error", Value(Text state.Error)
      "hasError", Value(Flag(state.Error <> ""))
      "captureDisabled", Value(Flag(captureDisabled state))
      "itemCount", Value(Count(List.length state.Items))
      "isEmpty", Value(Flag(List.isEmpty state.Items))
      "hasItems", Value(Flag(not (List.isEmpty state.Items)))
      "items", Rows(state.Items |> List.map itemRow) ]

// ---------------------------------------------------------------------------
// Effects - what the engine may ask the kernel to do
// ---------------------------------------------------------------------------

/// One constructor per legal operation, rather than one record with optional
/// fields. A closed algebra is what Limen's protocol actually describes, and
/// keeping it closed across the boundary is the Host Contract rule in
/// `.sde/architecture/BOUNDARY-PRESERVATION.md`.
///
/// Nothing constructs these yet: no effect is requested until the connection
/// flow lands (VIG-SEC-001). They exist now so that day extends a closed set
/// instead of opening one.
type HttpMethod =
    | Get
    | Put
    | Post
    | Patch
    | Delete

type StorageOperation =
    | StorageGet of key: string
    | StorageSet of key: string * value: string
    | StorageRemove of key: string

type HttpEffect =
    { CorrelationId: string
      Method: HttpMethod
      Url: string
      Headers: (string * string) list
      Body: string option
      TimeoutMs: int }

type Effect =
    | Http of HttpEffect
    | Storage of correlationId: string * operation: StorageOperation

/// The effects this step is asking for, and the ones it is abandoning.
///
/// Both are empty for now, and both go through the closed rendering below
/// rather than being hard-coded as `[]` at the wire.
let pendingEffects (_: State) : Effect list = []

let pendingCancellations (_: State) : string list = []

// ---------------------------------------------------------------------------
// The JSON boundary
// ---------------------------------------------------------------------------

/// Reads one browser-to-engine message and returns the command it implies.
///
/// `Initialize` and `EffectResult` are understood and currently carry no
/// command: initialization needs no work yet, and no effect has been requested
/// for a result to belong to.
let private readCommand (json: string) =
    // Named `parsed`, not `document`: Limen's boundary check matches the
    // identifier textually, and `document` is a browser global.
    use parsed = JsonDocument.Parse json
    let root = parsed.RootElement

    let kind =
        match root.TryGetProperty "kind" with
        | true, k when k.ValueKind = JsonValueKind.String -> k.GetString()
        | _ -> null

    match kind with
    | "Event" ->
        match root.TryGetProperty "event" with
        | true, e ->
            let name =
                match e.TryGetProperty "name" with
                | true, n when n.ValueKind = JsonValueKind.String ->
                    match n.GetString() with
                    | NonNull text -> text
                    | Null -> ""
                | _ -> ""

            let value =
                match e.TryGetProperty "value" with
                | true, v when v.ValueKind = JsonValueKind.String ->
                    match v.GetString() with
                    | NonNull text -> Some text
                    | Null -> None
                | _ -> None

            eventToCommand name value
        | _ -> Ignored
    | _ -> Ignored

/// Writes one primitive under a name.
///
/// Exhaustive by construction: adding a case to `ViewPrimitive` fails to
/// compile until it is given a rendering here.
let private writePrimitive (w: Utf8JsonWriter) (name: string) value =
    match value with
    | Text text -> w.WriteString(name, text)
    | Flag flag -> w.WriteBoolean(name, flag)
    | Count number -> w.WriteNumber(name, number)

let private writeViewValue (w: Utf8JsonWriter) (name: string) value =
    match value with
    | Value primitive -> writePrimitive w name primitive
    | Rows rows ->
        w.WriteStartArray name

        for row in rows do
            w.WriteStartObject()

            for field, primitive in row do
                writePrimitive w field primitive

            w.WriteEndObject()

        w.WriteEndArray()

let private methodName =
    function
    | Get -> "GET"
    | Put -> "PUT"
    | Post -> "POST"
    | Patch -> "PATCH"
    | Delete -> "DELETE"

let private writeEffect (w: Utf8JsonWriter) effect =
    w.WriteStartObject()

    match effect with
    | Http http ->
        w.WriteString("kind", "Http")
        w.WriteString("correlationId", http.CorrelationId)
        w.WriteString("method", methodName http.Method)
        w.WriteString("url", http.Url)

        match http.Headers with
        | [] -> ()
        | headers ->
            w.WriteStartObject "headers"

            for name, value in headers do
                w.WriteString(name, value)

            w.WriteEndObject()

        match http.Body with
        | Some body -> w.WriteString("body", body)
        | None -> ()

        w.WriteNumber("timeoutMs", http.TimeoutMs)
    | Storage(correlationId, operation) ->
        w.WriteString("kind", "Storage")
        w.WriteString("correlationId", correlationId)

        match operation with
        | StorageGet key ->
            w.WriteString("operation", "get")
            w.WriteString("key", key)
        | StorageSet(key, value) ->
            w.WriteString("operation", "set")
            w.WriteString("key", key)
            w.WriteString("value", value)
        | StorageRemove key ->
            w.WriteString("operation", "remove")
            w.WriteString("key", key)

    w.WriteEndObject()

/// Serialises the engine-to-browser message.
///
/// Written by hand rather than reflected over an anonymous record. That is the
/// Host Contract rule in `.sde/architecture/BOUNDARY-PRESERVATION.md`, and it
/// is not merely doctrine here: `JsonSerializer.Serialize` needs reflection,
/// which a trimmed WebAssembly publish disables, so the reflected version threw
/// `JsonSerializerIsReflectionDisabled` on the first dispatch and the page never
/// rendered. Every mechanical check was green while it did.
///
/// `effects` and `cancellations` are always present and currently always empty.
/// Emitting them rather than omitting them keeps the wire shape stable for the
/// kernel, which reads all three keys.
let private writeResponse state =
    use stream = new IO.MemoryStream()

    (use writer = new Utf8JsonWriter(stream)

     writer.WriteStartObject()

     writer.WriteStartObject "view"

     for key, value in project state do
         writeViewValue writer key value

     writer.WriteEndObject()

     writer.WriteStartArray "effects"

     for effect in pendingEffects state do
         writeEffect writer effect

     writer.WriteEndArray()

     writer.WriteStartArray "cancellations"

     for correlationId in pendingCancellations state do
         writer.WriteStringValue(correlationId: string)

     writer.WriteEndArray()

     writer.WriteEndObject())

    Encoding.UTF8.GetString(stream.ToArray())

/// The engine's entire surface: JSON in, JSON out.
///
/// State is threaded by the caller rather than held here, so the function stays
/// pure and testable without a browser or a WASM host.
let step (state: State) (messageJson: string) =
    let next = apply (readCommand messageJson) state
    next, writeResponse next

/// Mutable entry point for the WASM shim, which cannot thread state itself.
///
/// The shim holds no state and makes no decision; it forwards a string. The
/// engine owns the state, which is what Limen requires of the authority side.
let mutable private current = initial

let handle (messageJson: string) =
    let next, response = step current messageJson
    current <- next
    response

/// Test seam: resets the module-level state so a test starts from a known
/// point. Not part of the browser contract.
let reset () = current <- initial
