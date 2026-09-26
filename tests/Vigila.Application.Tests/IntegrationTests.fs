/// The Echelon `followup.create` boundary (VIG-15).
///
/// The ledger here is an in-memory stand-in whose claim is atomic, which is
/// the property the port demands. Durability across a restart is the host
/// ledger's job and is proven in Vigila.Host.GitHub.Tests.
///
/// Requirements: VIG-AGT-012, VIG-AGT-014, VIG-AGT-015, VIG-AGT-023,
/// VIG-AGT-050, VIG-TIME-002.
module Vigila.Application.IntegrationTests

open System
open System.Collections.Concurrent
open System.Reflection
open System.Text.Json
open System.Threading.Tasks
open Aegis
open Xunit
open Vigila.Application.Integration
open Vigila.Semantic.Actors
open Vigila.Semantic.Identifiers
open Vigila.Semantic.Item
open Vigila.Semantic.Items
open Vigila.Semantic.Tags
open Vigila.Semantic.Time

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

let private aegis =
    match Bootstrap.validate None (Aegis.configure "Vigila" None [ Sinks.standardError ]) with
    | Ok valid -> valid
    | Result.Error problems -> failwith $"%A{problems}"

let private at (text: string) =
    Instant.ofDateTimeOffset (DateTimeOffset.Parse(text, Globalization.CultureInfo.InvariantCulture))

let private instant = at "2026-09-25T15:00:00Z"
let private clock = Clock.fixedAt instant

/// Atomic in-memory ledger: GetOrAdd gives exactly one winner per id.
type private AtomicLedger() =
    let records = ConcurrentDictionary<string, FollowUpRecord>()
    let mutable calls = 0

    member _.Count = records.Count
    member _.Calls = calls

    interface FollowUpLedger with
        member _.Record record =
            Threading.Interlocked.Increment(&calls) |> ignore
            let winner = records.GetOrAdd(record.Envelope.OperationId, record)

            if obj.ReferenceEquals(winner, record) then
                Ok Recorded
            else
                Ok(AlreadyRecorded winner)

let private failingLedger failure =
    { new FollowUpLedger with
        member _.Record _ = Error failure }

let private throwingLedger =
    { new FollowUpLedger with
        member _.Record _ = raise (TimeoutException "transport timed out") }

let private agent =
    { Kind = AgentKind
      Provider = Known "openai"
      Identity = Known "gpt-5.6-sol"
      RunId = Known "run-15"
      SessionId = Unknown }

let private source =
    { Repository = Known "kemiller2002/vigila"
      Branch = Known "feature/echelon-followup-integration-15"
      Commit = Known "656a04b"
      WorkItem = Known "15" }

let private request operationId =
    { Envelope =
        { OperationId = operationId
          CorrelationId = "corr-15"
          Timestamp = instant
          Actor = agent
          Source = Some source }
      FollowUp =
        { Title = "Review integration result"
          Reason = "Agent discovered a decision that needs human review."
          RequestedAction = Review
          Priority = Normal
          ReviewAfter = None
          DueAt = None
          Tags = [ "integration" ]
          Context = None } }

let private withFollowUp f (r: CreateRequest) = { r with FollowUp = f r.FollowUp }
let private withEnvelope f (r: CreateRequest) = { r with Envelope = f r.Envelope }

let private create ledger r = Integration.create aegis clock ledger r

let private created outcome =
    match outcome with
    | Created record -> record
    | other -> failwith $"Expected Created, got %A{other}"

let private rejected outcome =
    match outcome with
    | Rejected refusals -> refusals
    | other -> failwith $"Expected Rejected, got %A{other}"

let private fields refusals = refusals |> List.map (fun r -> r.Field)

// ---------------------------------------------------------------------------
// 1-2. Creation and semantic mapping
// ---------------------------------------------------------------------------

[<Fact>]
let ``a valid request creates one follow-up`` () =
    let ledger = AtomicLedger()
    let outcome = create ledger (request "op-1")

    created outcome |> ignore
    Assert.Equal(1, ledger.Count)
    Assert.Equal("Created", CreateOutcome.code outcome)
    Assert.True(CreateOutcome.isSuccess outcome)

[<Fact>]
let ``the contract maps onto Vigila item semantics`` () =
    let item = (create (AtomicLedger()) (request "op-map") |> created).Item

    Assert.Equal(ItemKind.FollowUp, item.Kind)
    Assert.Equal(ItemStatus.Open, item.Status)
    Assert.Equal("Review integration result", Title.value item.Title)
    Assert.Equal(Some "Agent discovered a decision that needs human review.", item.Description)
    Assert.Equal(Some "Review", item.NextAction)
    Assert.Equal(CreatedVia.Integration, item.CreatedVia)
    Assert.Equal<string list>([ "integration" ], TagSet.toSortedList item.Tags)
    Assert.Equal(instant, item.CreatedAt)

[<Theory>]
[<InlineData("decide", "Decide")>]
[<InlineData("approve", "Approve")>]
[<InlineData("provide-information", "Provide information")>]
[<InlineData("investigate", "Investigate")>]
[<InlineData("other", "Follow up")>]
let ``every requested action is preserved as the next action`` (_: string, expected: string) =
    let action =
        match expected with
        | "Decide" -> Decide
        | "Approve" -> Approve
        | "Provide information" -> ProvideInformation
        | "Investigate" -> Investigate
        | _ -> RequestedAction.Other

    let record =
        create (AtomicLedger()) (request "op-action" |> withFollowUp (fun f -> { f with RequestedAction = action }))
        |> created

    Assert.Equal(Some expected, record.Item.NextAction)
    Assert.Equal(action, record.FollowUp.RequestedAction)

[<Fact>]
let ``an agent's follow-up needs review and a human's does not`` () =
    // VIG-AGT-023: the calling policy decides. An agent or automation asking
    // for human attention is exactly that policy; a human filing their own
    // follow-up is not asking for their capture to be reviewed.
    let byAgent = (create (AtomicLedger()) (request "op-agent") |> created).Item

    let byHuman =
        (create
            (AtomicLedger())
            (request "op-human"
             |> withEnvelope (fun e -> { e with Actor = { e.Actor with Kind = HumanKind } }))
         |> created)
            .Item

    Assert.True(byAgent.NeedsReview)
    Assert.False(byHuman.NeedsReview)
    Assert.Equal(ActorType.Human, byHuman.CreatedBy.Type)

[<Fact>]
let ``the contract context is kept verbatim rather than discarded`` () =
    let context = """{"decision":"retention","options":[1,2]}"""

    let record =
        create (AtomicLedger()) (request "op-ctx" |> withFollowUp (fun f -> { f with Context = Some context }))
        |> created

    Assert.Equal(Some context, record.FollowUp.Context)

// ---------------------------------------------------------------------------
// 3-4. Idempotency
// ---------------------------------------------------------------------------

[<Fact>]
let ``replaying an operation id returns the existing item and creates nothing`` () =
    let ledger = AtomicLedger()
    let first = create ledger (request "op-retry")
    let second = create ledger (request "op-retry")

    match first, second with
    | Created original, Replayed replayed ->
        Assert.Equal(original.Item.Id, replayed.Item.Id)
        Assert.Equal(1, ledger.Count)
        Assert.Equal("OperationAlreadyProcessed", CreateOutcome.code second)
        Assert.True(CreateOutcome.isSuccess second)
    | other -> failwith $"Expected Created then Replayed, got %A{other}"

[<Fact>]
let ``a replay with a later envelope timestamp is still a replay`` () =
    // A retry is a new attempt of the same operation; its own timestamp is
    // not part of what was asked for.
    let ledger = AtomicLedger()
    let original = create ledger (request "op-later") |> created

    let retry =
        create
            ledger
            (request "op-later"
             |> withEnvelope (fun e -> { e with Timestamp = at "2026-09-25T15:05:00Z" }))

    match retry with
    | Replayed record -> Assert.Equal(original.Item.Id, record.Item.Id)
    | other -> failwith $"Expected Replayed, got %A{other}"

[<Fact>]
let ``reusing an operation id for a different request is a conflict`` () =
    let ledger = AtomicLedger()
    let original = create ledger (request "op-reuse") |> created

    let reused =
        create ledger (request "op-reuse" |> withFollowUp (fun f -> { f with Title = "Something else" }))

    match reused with
    | Conflicted existing ->
        Assert.Equal(original.Item.Id, existing.Item.Id)
        Assert.Equal(1, ledger.Count)
        Assert.Equal("Conflict", CreateOutcome.code reused)
        Assert.False(CreateOutcome.isSuccess reused)
    | other -> failwith $"Expected Conflicted, got %A{other}"

[<Fact>]
let ``simultaneous invocations with one operation id create exactly one item`` () =
    let ledger = AtomicLedger()

    let outcomes =
        Array.init 64 (fun _ -> Task.Run(fun () -> create ledger (request "op-race")))
        |> Task.WhenAll
        |> fun t -> t.Result

    let createdCount = outcomes |> Array.filter (function Created _ -> true | _ -> false) |> Array.length
    let replayedCount = outcomes |> Array.filter (function Replayed _ -> true | _ -> false) |> Array.length

    let ids =
        outcomes
        |> Array.choose (function
            | Created r
            | Replayed r -> Some(ItemId.toGuid r.Item.Id)
            | _ -> None)
        |> Array.distinct

    Assert.Equal(1, createdCount)
    Assert.Equal(63, replayedCount)
    Assert.Equal(1, ids.Length)
    Assert.Equal(1, ledger.Count)

// ---------------------------------------------------------------------------
// 5-6. Actor provenance
// ---------------------------------------------------------------------------

[<Fact>]
let ``known agent provenance is preserved`` () =
    let record = create (AtomicLedger()) (request "op-known") |> created

    Assert.Equal(ActorType.Agent, record.Item.CreatedBy.Type)
    Assert.Equal("openai:gpt-5.6-sol", record.Item.CreatedBy.Name)
    Assert.Equal(Known "openai", record.Envelope.Actor.Provider)
    Assert.Equal(Known "gpt-5.6-sol", record.Envelope.Actor.Identity)
    Assert.Equal(Known "run-15", record.Envelope.Actor.RunId)
    Assert.Equal(Unknown, record.Envelope.Actor.SessionId)
    Assert.Equal("corr-15", record.Envelope.CorrelationId)
    Assert.Equal("op-known", record.Envelope.OperationId)
    Assert.Equal(instant, record.Envelope.Timestamp)

[<Fact>]
let ``unknown provenance is recorded as unknown and never fabricated`` () =
    let anonymous =
        request "op-unknown"
        |> withEnvelope (fun e ->
            { e with
                Actor =
                    { Kind = AgentKind
                      Provider = Unknown
                      Identity = Unknown
                      RunId = NotApplicable
                      SessionId = Unknown }
                Source = None })

    let record = create (AtomicLedger()) anonymous |> created

    Assert.Equal("unknown agent", record.Item.CreatedBy.Name)
    Assert.Equal(Unknown, record.Envelope.Actor.Provider)
    Assert.Equal(Unknown, record.Envelope.Actor.Identity)
    Assert.Equal(NotApplicable, record.Envelope.Actor.RunId)
    Assert.Equal(None, record.Envelope.Source)
    // Only the operation reference; no source was invented.
    Assert.Equal<string list>([ "echelon.operation" ], record.Item.Sources |> List.map (fun s -> s.Type))

[<Fact>]
let ``a system actor is recorded as an integration actor`` () =
    let record =
        create
            (AtomicLedger())
            (request "op-system"
             |> withEnvelope (fun e ->
                 { e with
                     Actor =
                         { e.Actor with
                             Kind = SystemKind
                             Provider = Known "praxis"
                             Identity = Unknown } }))
        |> created

    Assert.Equal(ActorType.Integration, record.Item.CreatedBy.Type)
    Assert.Equal("unknown system", record.Item.CreatedBy.Name)

// ---------------------------------------------------------------------------
// 7-11. Validation, and 15. no partial state
// ---------------------------------------------------------------------------

/// A ledger that fails the test if it is touched: a refused request must
/// never reach persistence.
let private untouchable =
    { new FollowUpLedger with
        member _.Record _ = failwith "A refused request reached the ledger." }

let private refusedFields r = create untouchable r |> rejected |> fields

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
let ``a missing operation id is refused`` (operationId: string) =
    Assert.Contains("operationId", refusedFields (request operationId))

[<Theory>]
[<InlineData("")>]
[<InlineData("  ")>]
let ``a missing correlation id is refused`` (correlationId: string) =
    let r = request "op-corr" |> withEnvelope (fun e -> { e with CorrelationId = correlationId })
    Assert.Contains("correlationId", refusedFields r)

[<Fact>]
let ``an empty title is refused`` () =
    let r = request "op-title" |> withFollowUp (fun f -> { f with Title = "   " })
    Assert.Contains("title", refusedFields r)

[<Fact>]
let ``a title beyond the contract's 240 characters is refused`` () =
    let r = request "op-long" |> withFollowUp (fun f -> { f with Title = String('x', 241) })
    Assert.Contains("title", refusedFields r)

[<Fact>]
let ``a missing reason is refused`` () =
    let r = request "op-reason" |> withFollowUp (fun f -> { f with Reason = " " })
    Assert.Contains("reason", refusedFields r)

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
let ``an empty tag is refused`` (tag: string) =
    let r = request "op-tag" |> withFollowUp (fun f -> { f with Tags = [ "ok"; tag ] })
    Assert.Contains("tags", refusedFields r)

[<Fact>]
let ``an over-long tag is refused`` () =
    let r = request "op-tag-long" |> withFollowUp (fun f -> { f with Tags = [ String('t', 65) ] })
    Assert.Contains("tags", refusedFields r)

[<Fact>]
let ``a duplicated tag is refused as the contract requires unique tags`` () =
    let r = request "op-tag-dup" |> withFollowUp (fun f -> { f with Tags = [ "a"; "a" ] })
    Assert.Contains("tags", refusedFields r)

[<Fact>]
let ``a provenance value marked known with no value is refused`` () =
    let r =
        request "op-known-empty"
        |> withEnvelope (fun e -> { e with Actor = { e.Actor with Identity = Known " " } })

    Assert.Contains("actor.identity", refusedFields r)

[<Fact>]
let ``every validation failure is reported at once`` () =
    let r =
        request ""
        |> withEnvelope (fun e -> { e with CorrelationId = "" })
        |> withFollowUp (fun f -> { f with Title = ""; Reason = ""; Tags = [ "" ] })

    let refused = refusedFields r

    for field in [ "operationId"; "correlationId"; "title"; "reason"; "tags" ] do
        Assert.Contains(field, refused)

[<Fact>]
let ``a refused request leaves no state behind`` () =
    let ledger = AtomicLedger()
    let bad = request "op-partial" |> withFollowUp (fun f -> { f with Reason = "" })

    create ledger bad |> rejected |> ignore

    Assert.Equal(0, ledger.Calls)
    Assert.Equal(0, ledger.Count)
    // The same operation id is still free for a corrected request.
    create ledger (request "op-partial") |> created |> ignore

// ---------------------------------------------------------------------------
// 12. Due versus review-after
// ---------------------------------------------------------------------------

[<Fact>]
let ``due and review-after stay distinct`` () =
    let reviewAfter = at "2026-10-01T09:00:00Z"
    let dueAt = at "2026-10-15T17:00:00Z"

    let item =
        (create
            (AtomicLedger())
            (request "op-dates"
             |> withFollowUp (fun f ->
                 { f with
                     ReviewAfter = Some reviewAfter
                     DueAt = Some dueAt }))
         |> created)
            .Item

    Assert.Equal(Some(WhenValue.AtInstant reviewAfter), item.FollowUp)
    Assert.Equal(Some(WhenValue.AtInstant dueAt), item.Due)

[<Fact>]
let ``review-after alone does not invent a due date`` () =
    let reviewAfter = at "2026-10-01T09:00:00Z"

    let item =
        (create (AtomicLedger()) (request "op-review-only" |> withFollowUp (fun f -> { f with ReviewAfter = Some reviewAfter }))
         |> created)
            .Item

    Assert.Equal(None, item.Due)
    Assert.Equal(Some(WhenValue.AtInstant reviewAfter), item.FollowUp)

// ---------------------------------------------------------------------------
// 13. Priority
// ---------------------------------------------------------------------------

[<Fact>]
let ``high and urgent are important and the exact priority is kept`` () =
    let outcomeFor priority =
        create (AtomicLedger()) (request "op-priority" |> withFollowUp (fun f -> { f with Priority = priority }))
        |> created

    let results = [ Low; Normal; High; Urgent ] |> List.map (fun p -> p, outcomeFor p)

    for priority, record in results do
        Assert.Equal(priority, record.FollowUp.Priority)
        Assert.Equal((priority = High || priority = Urgent), record.Item.Important)

// ---------------------------------------------------------------------------
// 14. Source provenance
// ---------------------------------------------------------------------------

[<Fact>]
let ``every known source value becomes a source reference`` () =
    let item = (create (AtomicLedger()) (request "op-source") |> created).Item

    let byType =
        item.Sources |> List.map (fun s -> s.Type, s.ExternalId) |> Map.ofList

    Assert.Equal(Some(Some "op-source"), byType.TryFind "echelon.operation")
    Assert.Equal(Some(Some "kemiller2002/vigila"), byType.TryFind "echelon.source.repository")
    Assert.Equal(Some(Some "feature/echelon-followup-integration-15"), byType.TryFind "echelon.source.branch")
    Assert.Equal(Some(Some "656a04b"), byType.TryFind "echelon.source.commit")
    Assert.Equal(Some(Some "15"), byType.TryFind "echelon.source.work-item")

[<Fact>]
let ``an unknown source value produces no reference`` () =
    let item =
        (create
            (AtomicLedger())
            (request "op-partial-source"
             |> withEnvelope (fun e ->
                 { e with
                     Source =
                         Some
                             { source with
                                 Commit = Unknown
                                 Branch = NotApplicable } }))
         |> created)
            .Item

    let types = item.Sources |> List.map (fun s -> s.Type)
    Assert.DoesNotContain("echelon.source.commit", types)
    Assert.DoesNotContain("echelon.source.branch", types)
    Assert.Contains("echelon.source.work-item", types)

// ---------------------------------------------------------------------------
// Operational failure is never success
// ---------------------------------------------------------------------------

[<Fact>]
let ``a ledger failure is reported as failure, not success`` () =
    let outcome =
        create
            (failingLedger
                { Code = "RepositoryUnavailable"
                  Retryable = true
                  Detail = "RepositoryUnavailable" })
            (request "op-fail")

    match outcome with
    | Failed fault ->
        Assert.Equal(FaultCode "VIGILA.INTEGRATION.REPOSITORYUNAVAILABLE", fault.Code)
        Assert.Equal("PersistenceFailed", CreateOutcome.code outcome)
        Assert.False(CreateOutcome.isSuccess outcome)
    | other -> failwith $"Expected Failed, got %A{other}"

[<Fact>]
let ``an unexpected ledger exception is captured by Aegis as a failure`` () =
    match create throwingLedger (request "op-throw") with
    | Failed fault -> Assert.Equal(FaultCode "VIGILA.INTEGRATION.UNEXPECTED", fault.Code)
    | other -> failwith $"Expected Failed, got %A{other}"

// ---------------------------------------------------------------------------
// Wire form: version negotiation, timestamps, unknown fields, receipts
// ---------------------------------------------------------------------------

let private invocation (contractVersion: string) (envelopeExtra: string) (payloadExtra: string) =
    $$"""
    {
      "capability": "followup.create",
      "contractVersion": {{contractVersion}},
      "envelope": {
        "schema": "echelon.execution-envelope/v1",
        "operationId": "op-wire",
        "correlationId": "corr-wire",
        "timestamp": "2026-09-25T15:00:00Z",
        "actor": {
          "kind": "agent",
          "provider": { "state": "known", "value": "anthropic" },
          "identity": { "state": "unknown" },
          "runId": { "state": "known", "value": "run-7" },
          "sessionId": { "state": "not-applicable" }
        },
        "source": {
          "repository": { "state": "known", "value": "kemiller2002/praxis" },
          "workItem": { "state": "known", "value": "42" }
        }{{envelopeExtra}}
      },
      "payload": {
        "title": "Decide on retention",
        "reason": "Two retention policies conflict.",
        "requestedAction": "decide",
        "priority": "urgent",
        "reviewAfter": "2026-10-01T09:00:00+02:00",
        "dueAt": null,
        "tags": ["Retention", "policy"],
        "context": { "policies": ["a", "b"] }{{payloadExtra}}
      }
    }
    """

let private receipt (json: string) = JsonDocument.Parse(json).RootElement

let private text (el: JsonElement) (name: string) =
    match el.GetProperty(name).GetString() with
    | NonNull s -> s
    | Null -> failwith $"'%s{name}' was null"

let private errorFields (el: JsonElement) =
    el.GetProperty("errors").EnumerateArray() |> Seq.map (fun e -> text e "field") |> Seq.toList

let private invoke ledger json =
    IntegrationWire.invoke aegis clock ledger json |> receipt

[<Fact>]
let ``a valid invocation decodes every contract field`` () =
    match IntegrationWire.decode (invocation "1" "" "") with
    | Ok r ->
        Assert.Equal("op-wire", r.Envelope.OperationId)
        Assert.Equal(AgentKind, r.Envelope.Actor.Kind)
        Assert.Equal(Known "anthropic", r.Envelope.Actor.Provider)
        Assert.Equal(Unknown, r.Envelope.Actor.Identity)
        Assert.Equal(NotApplicable, r.Envelope.Actor.SessionId)
        Assert.Equal(Some(Unknown), r.Envelope.Source |> Option.map (fun s -> s.Branch))
        Assert.Equal(Decide, r.FollowUp.RequestedAction)
        Assert.Equal(Urgent, r.FollowUp.Priority)
        Assert.Equal(Some(at "2026-10-01T07:00:00Z"), r.FollowUp.ReviewAfter)
        Assert.Equal(None, r.FollowUp.DueAt)
        Assert.Equal<string list>([ "Retention"; "policy" ], r.FollowUp.Tags)
        Assert.Equal(Some """{ "policies": ["a", "b"] }""", r.FollowUp.Context)
    | Error e -> failwith $"%A{e}"

[<Fact>]
let ``the wire boundary creates, then replays, with structured receipts`` () =
    let ledger = AtomicLedger()
    let first = invoke ledger (invocation "1" "" "")
    let second = invoke ledger (invocation "1" "" "")

    Assert.Equal("success", text first "status")
    Assert.Equal("Created", text first "code")
    Assert.Equal("OperationAlreadyProcessed", text second "code")
    Assert.Equal(text first "itemId", text second "itemId")
    Assert.Equal("op-wire", text second "operationId")
    Assert.Equal(1, ledger.Count)

[<Theory>]
[<InlineData("2")>]
[<InlineData("0")>]
let ``an unsupported contract version is refused as SchemaUnsupported`` (version: string) =
    let ledger = AtomicLedger()
    let r = invoke ledger (invocation version "" "")

    Assert.Equal("failure", text r "status")
    Assert.Equal("SchemaUnsupported", text r "code")
    Assert.Equal<string list>([ "contractVersion" ], errorFields r)
    Assert.Equal(0, ledger.Calls)

[<Fact>]
let ``an unsupported envelope schema is refused as SchemaUnsupported`` () =
    let json = (invocation "1" "" "").Replace("execution-envelope/v1", "execution-envelope/v2")
    let r = invoke (AtomicLedger()) json

    Assert.Equal("SchemaUnsupported", text r "code")
    Assert.Contains("envelope.schema", errorFields r)

[<Fact>]
let ``another capability is refused`` () =
    let json = (invocation "1" "" "").Replace("\"followup.create\"", "\"followup.resolve\"")
    Assert.Equal("SchemaUnsupported", text (invoke untouchable json) "code")

[<Theory>]
[<InlineData("tomorrow")>]
[<InlineData("2026-10-15")>]
[<InlineData("2026-10-15T17:00:00")>]
[<InlineData("2026-13-45T17:00:00Z")>]
let ``a malformed or offset-less timestamp is refused`` (value: string) =
    let json = (invocation "1" "" "").Replace("\"dueAt\": null", $"\"dueAt\": \"%s{value}\"")
    let r = invoke untouchable json

    Assert.Equal("ValidationFailed", text r "code")
    Assert.Contains("payload.dueAt", errorFields r)

[<Fact>]
let ``a malformed envelope timestamp is refused`` () =
    let json = (invocation "1" "" "").Replace("\"2026-09-25T15:00:00Z\"", "\"yesterday\"")
    Assert.Contains("envelope.timestamp", errorFields (invoke untouchable json))

[<Fact>]
let ``an unknown payload field is refused rather than silently dropped`` () =
    let r = invoke untouchable (invocation "1" "" ",\n\"assignee\": \"kevin\"")
    Assert.Contains("payload.assignee", errorFields r)

[<Fact>]
let ``an unknown envelope field is refused rather than silently dropped`` () =
    let r = invoke untouchable (invocation "1" ",\n\"token\": \"ghp_x\"" "")
    Assert.Contains("envelope.token", errorFields r)
    Assert.DoesNotContain("ghp_x", r.GetRawText())

[<Fact>]
let ``an unknown requested action or priority is refused`` () =
    let json =
        (invocation "1" "" "").Replace("\"decide\"", "\"delegate\"").Replace("\"urgent\"", "\"critical\"")

    let refused = errorFields (invoke untouchable json)
    Assert.Contains("payload.requestedAction", refused)
    Assert.Contains("payload.priority", refused)

[<Fact>]
let ``malformed JSON is a refusal, not an exception`` () =
    let r = invoke untouchable "{ not json"
    Assert.Equal("ValidationFailed", text r "code")

[<Fact>]
let ``a failure receipt carries an Aegis reference and never claims success`` () =
    let r =
        invoke
            (failingLedger
                { Code = "RateLimited"
                  Retryable = true
                  Detail = "RateLimited" })
            (invocation "1" "" "")

    Assert.Equal("failure", text r "status")
    Assert.Equal("PersistenceFailed", text r "code")
    Assert.StartsWith("AG-", text r "reference")
    Assert.False(r.TryGetProperty("itemId") |> fst)

// ---------------------------------------------------------------------------
// Independence conformance (Echelon Integration Standard section 1)
// ---------------------------------------------------------------------------

[<Fact>]
let ``Vigila references no registry or other Echelon application`` () =
    // Vigila alone is healthy: nothing it loads names the registry or another
    // Echelon system, so their absence cannot break it. Aegis is a shared
    // capability library, not an optional Echelon application.
    let forbidden = [ "registry"; "praxis"; "chrona"; "summa"; "ros"; "repositoryoperatingsystem"; "ordo" ]

    let referenced =
        [ typeof<CreateRequest>.Assembly; typeof<Item>.Assembly ]
        |> List.collect (fun a -> a.GetReferencedAssemblies() |> Array.toList)
        |> List.choose (fun n -> n.Name |> Option.ofObj)
        |> List.map (fun n -> n.ToLowerInvariant())

    for name in referenced do
        for f in forbidden do
            Assert.False(name.Split('.') |> Array.contains f, $"Vigila references '%s{name}'.")

[<Fact>]
let ``core capture works with no registry and no integration configured`` () =
    // The standalone scenario: the ordinary capture path runs with nothing
    // Echelon-related wired at all. Driven through the pure `step` so it
    // shares no module state with other tests.
    let event name value =
        $"""{{"kind":"Event","event":{{"kind":"Event","name":"%s{name}","value":"%s{value}"}}}}"""

    let typed, _ = Dispatch.step Dispatch.initial (event "titleChanged" "Standalone capture")
    let _, json = Dispatch.step typed """{"kind":"Event","event":{"kind":"Event","name":"capture"}}"""
    let view = JsonDocument.Parse(json).RootElement.GetProperty "view"

    Assert.Equal(1, view.GetProperty("itemCount").GetInt32())

[<Fact>]
let ``the provider boundary needs only its own ledger, never a registry`` () =
    // Registry unavailable: the boundary's only collaborator is Vigila's own
    // ledger port, so an absent registry cannot make it fail.
    // The signature is the proof: it is checked by the compiler, and it
    // names no registry, resolver or network collaborator.
    let boundary: AegisConfig -> Clock -> FollowUpLedger -> CreateRequest -> CreateOutcome =
        Integration.create

    boundary aegis clock (AtomicLedger()) (request "op-standalone") |> created |> ignore
