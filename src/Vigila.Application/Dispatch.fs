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

/// Builds the view.
///
/// `ViewState` admits primitives and flat item arrays only, so the projection
/// is deliberately shallow: notes, history and dates stay in the engine until a
/// view needs them.
let project state =
    let items =
        state.Items
        |> List.map (fun item ->
            {| id = ItemId.display item.Id
               title = Title.value item.Title
               kind = (match item.Kind with
                       | Task -> "Task"
                       | FollowUp -> "Follow-up"
                       | ItemKind.Waiting -> "Waiting")
               status = (match item.Status with
                         | Open -> "Open"
                         | ItemStatus.Waiting -> "Waiting"
                         | Deferred -> "Deferred"
                         | Completed -> "Completed"
                         | ItemStatus.Cancelled -> "Cancelled") |})

    {| draft = state.Draft
       error = state.Error
       hasError = state.Error <> ""
       captureDisabled = captureDisabled state
       itemCount = List.length state.Items
       isEmpty = List.isEmpty state.Items
       hasItems = not (List.isEmpty state.Items)
       items = items |}

// ---------------------------------------------------------------------------
// The JSON boundary
// ---------------------------------------------------------------------------

let private options = JsonSerializerOptions(WriteIndented = false)

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

/// Serialises the engine-to-browser message.
///
/// `effects` and `cancellations` are always present and currently always empty.
/// Emitting them rather than omitting them keeps the wire shape stable for the
/// kernel, which reads all three keys.
let private writeResponse state =
    let payload =
        {| view = project state
           effects = ([]: obj list)
           cancellations = ([]: string list) |}

    JsonSerializer.Serialize(payload, options)

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
