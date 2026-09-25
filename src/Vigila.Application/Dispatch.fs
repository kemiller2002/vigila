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
open Aegis
open Vigila.Semantic.Identifiers
open Vigila.Semantic.Time
open Vigila.Semantic.Actors
open Vigila.Semantic.Items
open Vigila.Semantic.Item
open Vigila.Application.Effects
open Vigila.Application.Connection

/// The engine's authoritative state.
///
/// `Outbox` and `Awaiting` are how a step communicates with the kernel. A step
/// is a pure function, so it cannot perform an effect; it can only leave one in
/// the outbox for the kernel to pick up, and remember what it is waiting for so
/// the eventual result can be matched to the request that caused it.
[<NoComparison>]
type State =
    { Items: Item list
      Draft: string
      Error: string
      Connection: Connection
      SetupRepository: string
      SetupBranch: string
      SetupTokenEntered: bool
      SetupError: string
      OperationalFault: Presentation.T option
      Outbox: Effect list
      Awaiting: Map<string, Awaiting>
      NextCorrelation: int }

/// What an outstanding effect was asked for.
///
/// Keyed by correlation id, because Limen's results arrive asynchronously and
/// carry nothing but that id. Without this the engine would have to guess which
/// request a result belonged to, and guessing is how a validation failure gets
/// attributed to the wrong step.
and Awaiting =
    | RestoringRepository
    | RestoringBranch
    | RestoringTokenPresence
    | ProbingRepositoryRead
    | ProbingBranchExists
    /// A write whose acknowledgement carries nothing the engine needs.
    ///
    /// Distinct from the `Restoring…` cases on purpose: a storage *write* is
    /// acknowledged with the same message shape as a storage *read*, so reusing
    /// a read's tag makes the acknowledgement look like "restored an empty
    /// value" and silently clears the field that was just saved.
    | AcknowledgingWrite

let initial =
    { Items = []
      Draft = ""
      Error = ""
      Connection = disconnected
      SetupRepository = ""
      SetupBranch = BranchName.Default
      SetupTokenEntered = false
      SetupError = ""
      OperationalFault = None
      Outbox = []
      Awaiting = Map.empty
      NextCorrelation = 0 }

/// The clock the engine hands to the domain.
///
/// Reading the wall clock is an effect, and VIG-TIME-023 keeps it out of domain
/// logic — so it is read here, at the composition root, and passed in. Limen's
/// protocol has no Time effect to route it through; if one is added later this
/// is the single place that changes.
let private clock =
    Clock.create (fun () -> Instant.ofDateTimeOffset DateTimeOffset.UtcNow)


/// Unexpected operational failure is classified once at the outer Limen/JSON
/// boundary. Expected domain refusals remain ordinary typed outcomes and never
/// become Aegis faults.
let private aegis =
    let configured = Aegis.configure "Vigila" None [ Sinks.standardError ]

    match Bootstrap.validate None configured with
    | Ok valid -> valid
    | Result.Error problems ->
        invalidOp $"Invalid Vigila Aegis configuration: %A{problems}"

let private classifyBoundaryFailure scope (ex: exn) =
    let code, category, userMessage =
        match ex with
        | :? JsonException ->
            FaultCode "VIGILA.BOUNDARY.MESSAGE_INVALID",
            DataFailure,
            "Vigila could not process an application message. The rest of the application is still available."
        | _ ->
            FaultCode "VIGILA.BOUNDARY.UNEXPECTED",
            IntegrationFailure,
            "Vigila encountered an unexpected operational problem. Your current application state was kept."

    Aegis.faultOf
        aegis
        scope
        code
        category
        FaultSeverity.Error
        OperationOnly
        Transient
        Continue
        userMessage
        ex

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
    | EditSetupRepository of string
    | EditSetupBranch of string
    | SetupTokenEntered of bool
    | Connect
    | Disconnect
    | Restore
    | StorageValue of correlationId: string * value: string option
    | HttpOutcome of correlationId: string * outcome: HttpOutcome
    | Ignored

/// What the kernel observed when it performed an Http effect.
///
/// `Unknown` is carried rather than folded into a failure because Limen's
/// protocol reports "dispatched but the outcome was never observed" as its own
/// case, and VIG-UI-014 forbids reporting an unobserved result as either a
/// success or a definite failure.
and HttpOutcome =
    | Responded of status: int * body: string
    | Unreachable
    | Unknown

/// Maps a semantic event to a command.
///
/// Unknown event names become `Ignored` rather than an error: the DOM is not
/// authoritative, and an event the engine does not understand is a binding
/// mistake, not a domain failure.
let eventToCommand (name: string) (value: string option) =
    match name with
    | "titleChanged" -> EditDraft(defaultArg value "")
    | "capture" -> Capture
    | "repositoryChanged" -> EditSetupRepository(defaultArg value "")
    | "branchChanged" -> EditSetupBranch(defaultArg value "")
    // The kernel reports only whether a token has been typed, never the token.
    | "tokenEntered" -> SetupTokenEntered(defaultArg value "" <> "")
    | "connect" -> Connect
    | "disconnect" -> Disconnect
    | _ -> Ignored

// ---------------------------------------------------------------------------
// Transitions - pure, and the only place these decisions are made
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Issuing effects
// ---------------------------------------------------------------------------

/// Allocates a correlation id and records what it is for.
///
/// Sequential rather than random: the engine is pure, a GUID would need a
/// source of entropy it deliberately does not have, and a deterministic id
/// makes a test able to name the request it is answering.
let private issue awaiting build state =
    let correlationId = $"c%d{state.NextCorrelation}"

    { state with
        Outbox = state.Outbox @ [ build correlationId ]
        Awaiting = Map.add correlationId awaiting state.Awaiting
        NextCorrelation = state.NextCorrelation + 1 }

let private issueStorageGet key awaiting state =
    issue awaiting (fun id -> Storage(id, StorageGet key)) state

let private issueHttp (probe: HttpEffect) awaiting state =
    issue awaiting (fun id -> Http { probe with CorrelationId = id }) state

/// The settings the setup form currently describes, or why it is not valid.
let private settingsFromSetup state =
    match RepositoryId.create state.SetupRepository with
    | Error refusal -> Error refusal
    | Ok repository ->
        match BranchName.create state.SetupBranch with
        | Error refusal -> Error refusal
        | Ok branch -> Ok { Repository = repository; Branch = branch }

/// Starts validation: prove the repository reads before anything else
/// (VIG-SEC-007 step 4, VIG-SEC-008).
let private beginValidation settings state =
    { state with
        Connection =
            { state.Connection with
                Settings = Some settings
                Status = Validating ProvingRead } }
    |> issueHttp (readProbe settings) ProbingRepositoryRead

let private refuse reason state =
    { state with
        Connection =
            { state.Connection with
                Status = Refused reason } }

/// Reads `permissions.push` from a repository response.
///
/// Absent or unreadable means "no write capability" rather than "assume yes":
/// VIG-SEC-009 forbids inferring write access from a successful read.
let private canPush (body: string) =
    try
        use parsed = JsonDocument.Parse body

        match parsed.RootElement.TryGetProperty "permissions" with
        | true, permissions ->
            match permissions.TryGetProperty "push" with
            | true, push -> push.ValueKind = JsonValueKind.True
            | _ -> false
        | _ -> false
    with _ ->
        false

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

    // -- setup form ---------------------------------------------------------
    | EditSetupRepository value -> { state with SetupRepository = value; SetupError = "" }
    | EditSetupBranch value -> { state with SetupBranch = value; SetupError = "" }
    | SetupTokenEntered entered -> { state with SetupTokenEntered = entered; SetupError = "" }

    // -- VIG-SEC-007 step 1: read stored configuration -----------------------
    | Restore ->
        state
        |> issueStorageGet Keys.Repository RestoringRepository
        |> issueStorageGet Keys.Branch RestoringBranch
        |> issueStorageGet Keys.TokenPresent RestoringTokenPresence

    | StorageValue(correlationId, value) ->
        match Map.tryFind correlationId state.Awaiting with
        | None -> state
        | Some awaiting ->
            let state = { state with Awaiting = Map.remove correlationId state.Awaiting }

            let restored =
                match awaiting with
                | RestoringRepository -> { state with SetupRepository = defaultArg value "" }
                | RestoringBranch ->
                    { state with
                        SetupBranch =
                            match value with
                            | Some branch when branch <> "" -> branch
                            | _ -> BranchName.Default }
                | RestoringTokenPresence ->
                    { state with
                        SetupTokenEntered = value = Some "yes"
                        Connection =
                            { state.Connection with
                                HasToken = value = Some "yes" } }
                | ProbingRepositoryRead
                | ProbingBranchExists
                | AcknowledgingWrite -> state

            // Only a restore can complete the startup sequence. A write
            // acknowledgement must not re-enter it: doing so restarted
            // validation on every save and left the connection stuck
            // "Checking…" forever.
            let wasRestore =
                match awaiting with
                | RestoringRepository
                | RestoringBranch
                | RestoringTokenPresence -> true
                | ProbingRepositoryRead
                | ProbingBranchExists
                | AcknowledgingWrite -> false

            let stillRestoring =
                restored.Awaiting
                |> Map.exists (fun _ a ->
                    a = RestoringRepository || a = RestoringBranch || a = RestoringTokenPresence)

            // VIG-SEC-007 step 3, and VIG-SEC-008: what was stored is attempted,
            // never assumed.
            if not wasRestore || stillRestoring then
                restored
            elif not restored.Connection.HasToken then
                restored
            else
                match settingsFromSetup restored with
                | Error _ -> restored
                | Ok settings -> beginValidation settings restored

    // -- VIG-SEC-007 step 3: connect -----------------------------------------
    | Connect ->
        if not state.SetupTokenEntered then
            { state with SetupError = describeConnectionRefusal TokenMissing }
        else
            match settingsFromSetup state with
            | Error refusal -> { state with SetupError = describeRefusal refusal }
            | Ok settings ->
                { state with
                    SetupError = ""
                    Connection = { state.Connection with HasToken = true } }
                |> beginValidation settings

    | Disconnect ->
        // VIG-SEC-006: clearing the token must not delete repository data, so
        // only the credential and the connection state are dropped. The
        // repository and branch stay, because re-entering them is friction with
        // no security benefit.
        { state with
            Connection =
                { state.Connection with
                    HasToken = false
                    Status = Unconfigured }
            SetupTokenEntered = false
            SetupError = ""
            Items = [] }
        |> issue AcknowledgingWrite (fun id -> Storage(id, StorageRemove Keys.TokenPresent))

    | HttpOutcome(correlationId, outcome) ->
        match Map.tryFind correlationId state.Awaiting with
        | None -> state
        | Some awaiting ->
            let state = { state with Awaiting = Map.remove correlationId state.Awaiting }

            match awaiting, outcome with
            | (ProbingRepositoryRead | ProbingBranchExists), Unreachable
            | (ProbingRepositoryRead | ProbingBranchExists), Unknown ->
                refuse RepositoryUnavailable state

            | ProbingRepositoryRead, Responded(status, body) ->
                match classifyStatus RepositoryNotFound status with
                | Error reason -> refuse reason state
                | Ok() ->
                    match classifyWriteCapability (canPush body) with
                    | Error reason -> refuse reason state
                    | Ok() ->
                        match state.Connection.Settings with
                        | None -> refuse RepositoryNotFound state
                        | Some settings ->
                            { state with
                                Connection =
                                    { state.Connection with
                                        Status = Validating ProvingWrite } }
                            |> issueHttp (branchProbe settings) ProbingBranchExists

            | ProbingBranchExists, Responded(status, _) ->
                match classifyStatus BranchUnavailable status with
                | Error reason -> refuse reason state
                | Ok() ->
                    // Every capability VIG-SEC-009 names for this slice is now
                    // proved, so the configuration is worth persisting
                    // (VIG-SEC-002). The token is not written here: the kernel
                    // already holds it.
                    let settings = state.Connection.Settings

                    let persisted =
                        match settings with
                        | None -> state
                        | Some s ->
                            state
                            |> issue AcknowledgingWrite (fun id ->
                                Storage(id, StorageSet(Keys.Repository, RepositoryId.value s.Repository)))
                            |> issue AcknowledgingWrite (fun id ->
                                Storage(id, StorageSet(Keys.Branch, BranchName.value s.Branch)))

                    { persisted with
                        Connection = { persisted.Connection with Status = Connected }
                        SetupError = "" }

            | (RestoringRepository | RestoringBranch | RestoringTokenPresence | AcknowledgingWrite), _ ->
                state

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
    let connection = state.Connection

    let statusText =
        match connection.Status with
        | Unconfigured -> ""
        | Validating ProvingRead -> "Checking the repository…"
        | Validating ProvingWrite -> "Checking the branch…"
        | Connected -> "Connected"
        | Refused reason -> describeConnectionRefusal reason

    // The setup error is whatever the user most recently needs to fix: a
    // refused configuration value, or a refused connection. They cannot both
    // be current, because editing a field clears the first and a new attempt
    // replaces the second.
    let setupError =
        if state.SetupError <> "" then
            state.SetupError
        else
            match connection.Status with
            | Refused reason -> describeConnectionRefusal reason
            | _ -> ""

    let validating =
        match connection.Status with
        | Validating _ -> true
        | _ -> false


    let operationalFaultTitle, operationalFaultMessage, operationalFaultSeverity, operationalFaultReference =
        match state.OperationalFault with
        | Some fault ->
            let severity =
                match fault.Severity with
                | FaultSeverity.Diagnostic -> "diagnostic"
                | FaultSeverity.Warning -> "warning"
                | FaultSeverity.Error -> "error"
                | FaultSeverity.Critical -> "critical"

            fault.Title, fault.Message, severity, fault.Reference
        | None -> "", "", "", ""

    [ "draft", Value(Text state.Draft)
      "operationalFaultTitle", Value(Text operationalFaultTitle)
      "operationalFaultMessage", Value(Text operationalFaultMessage)
      "operationalFaultSeverity", Value(Text operationalFaultSeverity)
      "operationalFaultReference", Value(Text operationalFaultReference)
      "hasOperationalFault", Value(Flag state.OperationalFault.IsSome)
      "error", Value(Text state.Error)
      "hasError", Value(Flag(state.Error <> ""))
      "captureDisabled", Value(Flag(captureDisabled state))
      "itemCount", Value(Count(List.length state.Items))
      "isEmpty", Value(Flag(List.isEmpty state.Items))
      "hasItems", Value(Flag(not (List.isEmpty state.Items)))
      "items", Rows(state.Items |> List.map itemRow)

      // -- connection -------------------------------------------------------
      "setupRepository", Value(Text state.SetupRepository)
      "setupBranch", Value(Text state.SetupBranch)
      "connectionStatus", Value(Text statusText)
      "connectionCode",
      Value(
          Text(
              match connection.Status with
              | Refused reason -> refusalCode reason
              | _ -> ""
          )
      )
      "setupError", Value(Text setupError)
      "hasSetupError", Value(Flag(setupError <> ""))
      "isValidating", Value(Flag validating)
      "connectDisabled", Value(Flag(validating || not state.SetupTokenEntered))
      "needsSetup", Value(Flag(needsSetup connection))
      "isConnected", Value(Flag(connection.Status = Connected))
      "hasToken", Value(Flag connection.HasToken) ]

// ---------------------------------------------------------------------------
// The JSON boundary
// ---------------------------------------------------------------------------

/// Reads one browser-to-engine message and returns the command it implies.
///
/// `LocationChanged` is understood and carries no command: Vigila has no
/// routing, so a location change is not a domain fact. It becomes `Ignored`
/// rather than an error, because an unrecognised message is the kernel's
/// business and not a domain failure.
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

    // VIG-SEC-007 step 1: startup begins by reading stored configuration.
    | "Initialize" -> Restore

    | "EffectResult" ->
        match root.TryGetProperty "result" with
        | true, result ->
            let correlationId =
                match result.TryGetProperty "correlationId" with
                | true, c when c.ValueKind = JsonValueKind.String ->
                    match c.GetString() with
                    | NonNull text -> text
                    | Null -> ""
                | _ -> ""

            let resultKind =
                match result.TryGetProperty "kind" with
                | true, k when k.ValueKind = JsonValueKind.String -> k.GetString()
                | _ -> null

            let outcome =
                match result.TryGetProperty "outcome" with
                | true, o -> Some o
                | _ -> None

            let outcomeKind (o: JsonElement) =
                match o.TryGetProperty "kind" with
                | true, k when k.ValueKind = JsonValueKind.String -> k.GetString()
                | _ -> null

            match resultKind, outcome with
            | "StorageResult", Some o ->
                match outcomeKind o with
                | "Success" ->
                    let value =
                        match o.TryGetProperty "value" with
                        | true, v when v.ValueKind = JsonValueKind.String ->
                            match v.GetString() with
                            | NonNull text -> Some text
                            | Null -> None
                        | _ -> None

                    StorageValue(correlationId, value)
                // A storage failure is indistinguishable from "nothing stored"
                // for this purpose: either way there is no configuration to
                // restore, and VIG-SEC-007 step 2 sends the user to setup.
                | _ -> StorageValue(correlationId, None)

            | "HttpResult", Some o ->
                match outcomeKind o with
                | "Success" ->
                    let status =
                        match o.TryGetProperty "status" with
                        | true, st when st.ValueKind = JsonValueKind.Number -> st.GetInt32()
                        | _ -> 0

                    let body =
                        match o.TryGetProperty "body" with
                        | true, b when b.ValueKind = JsonValueKind.String ->
                            match b.GetString() with
                            | NonNull text -> text
                            | Null -> ""
                        | true, b -> b.GetRawText()
                        | _ -> ""

                    HttpOutcome(correlationId, Responded(status, body))
                // Limen reports a dispatched-but-unobserved request as its own
                // case, and VIG-UI-014 forbids calling that either outcome.
                | "OutcomeUnknown" -> HttpOutcome(correlationId, Unknown)
                | _ -> HttpOutcome(correlationId, Unreachable)

            | _ -> Ignored
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
    | Clipboard(correlationId, text) ->
        w.WriteString("kind", "Clipboard")
        w.WriteString("correlationId", correlationId)
        w.WriteString("operation", "writeText")
        w.WriteString("text", text)
    | Navigation(correlationId, operation) ->
        w.WriteString("kind", "Navigation")
        w.WriteString("correlationId", correlationId)

        match operation with
        | NavigatePush url ->
            w.WriteString("operation", "push")
            w.WriteString("url", url)
        | NavigateReplace url ->
            w.WriteString("operation", "replace")
            w.WriteString("url", url)
        | NavigateBack -> w.WriteString("operation", "back")
        | NavigateForward -> w.WriteString("operation", "forward")

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

     for effect in state.Outbox do
         writeEffect writer effect

     writer.WriteEndArray()

     writer.WriteStartArray "cancellations"

     // Nothing is cancelled yet; the key is emitted because the kernel reads
     // all three and a stable wire shape is cheaper than a conditional one.
     for correlationId in ([]: string list) do
         writer.WriteStringValue correlationId

     writer.WriteEndArray()

     writer.WriteEndObject())

    Encoding.UTF8.GetString(stream.ToArray())

/// The engine's entire surface: JSON in, JSON out.
///
/// State is threaded by the caller rather than held here, so the function stays
/// pure and testable without a browser or a WASM host.
let step (state: State) (messageJson: string) =
    // The outbox is cleared before the command runs, so a step emits exactly
    // the effects that step decided on. Carrying yesterday's effects forward
    // would re-issue a request the kernel has already performed.
    let next = apply (readCommand messageJson) { state with Outbox = [] }
    next, writeResponse next

/// Mutable entry point for the WASM shim, which cannot thread state itself.
///
/// The shim holds no state and makes no decision; it forwards a string. The
/// engine owns the state, which is what Limen requires of the authority side.
let mutable private current = initial

let handle (messageJson: string) =
    let scope = Aegis.scope aegis "Vigila.Application.Dispatch.handle" Map.empty
    let cleared = { current with OperationalFault = None }

    match Aegis.capture aegis scope classifyBoundaryFailure (fun () -> step cleared messageJson) with
    | Ok(next, response) ->
        current <- next
        response
    | Result.Error fault ->
        let presentation = Presentation.present "Vigila could not complete that operation" fault
        let failed =
            { current with
                OperationalFault = Some presentation
                Outbox = [] }

        current <- failed
        writeResponse failed

/// Test seam: resets the module-level state so a test starts from a known
/// point. Not part of the browser contract.
let reset () = current <- initial
