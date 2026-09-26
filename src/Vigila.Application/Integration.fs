/// Tier 3 - Echelon integration boundary for `followup.create` v1.
///
/// Vigila is the provider of `followup.create` (echelon-registry,
/// `contracts/followup-create.v1.schema.json`). This module owns the
/// translation from that ecosystem-level contract into Vigila semantics:
/// callers submit a semantic request and an execution envelope, and never
/// construct Vigila identifiers, items or persistence artifacts
/// (VIG-PER-001).
///
/// Nothing here consults a registry or any other Echelon system. Registry
/// discovery belongs to consumers; Vigila's core behaviour and this boundary
/// both work with no registry present.
///
/// Mapping, stated once so it is reviewable:
///
///   followup.create           Vigila Item
///   ------------------------  -----------------------------------------------
///   (capability)              Kind = FollowUp, CreatedVia = Integration
///   title                     Title (Vigila rules plus the contract's 240 cap)
///   reason                    Description
///   requestedAction           NextAction, as display text
///   priority                  Important = high | urgent (VIG-DOM-038); the
///                             exact priority is kept in the operation record
///   reviewAfter               FollowUp date (VIG-TIME-004)
///   dueAt                     Due date (VIG-TIME-001); never conflated with
///                             reviewAfter (VIG-TIME-002)
///   tags                      Tags, normalised by Vigila's Tag rules
///   context                   kept verbatim in the operation record
///   envelope.actor            CreatedBy (kind + identity); provider, run and
///                             session ids are kept in the operation record
///   envelope.source           Sources, one reference per known value
///   envelope.operationId      idempotency key, and a Sources reference
///   envelope.correlationId    kept in the operation record
///   (actor kind <> human)     NeedsReview = true (VIG-AGT-023: the calling
///                             policy -- an agent or automation asking for
///                             human attention -- determines review is needed)
///
/// The operation record is the durable, lossless copy of the request; the item
/// carries what Vigila's own model can represent. Neither stuffs provenance
/// into free text.
///
/// Requirements: VIG-AGT-012, VIG-AGT-013, VIG-AGT-014, VIG-AGT-015,
/// VIG-AGT-023, VIG-AGT-035, VIG-AGT-050, VIG-DOM-037.
module Vigila.Application.Integration

open System
open Aegis
open Vigila.Semantic.Actors
open Vigila.Semantic.Item
open Vigila.Semantic.Items
open Vigila.Semantic.Time
open Vigila.Semantic.Tags

/// The capability this boundary provides.
[<Literal>]
let Capability = "followup.create"

/// The `followup.create` contract version this boundary implements.
[<Literal>]
let ContractVersion = 1

/// The execution envelope schema this boundary accepts.
[<Literal>]
let EnvelopeSchema = "echelon.execution-envelope/v1"

/// The contract's own title limit. Tighter than Vigila's (Title.MaxLength), so
/// a request the contract forbids is refused rather than silently accepted.
[<Literal>]
let MaxContractTitleLength = 240

/// A provenance value that distinguishes "known", "unknown" and "not
/// applicable", as the Echelon Integration Standard section 5 requires. An
/// unknown value is never replaced by a guess.
type KnownValue =
    | Known of string
    | Unknown
    | NotApplicable

type ActorKind =
    | AgentKind
    | HumanKind
    | AutomationKind
    | SystemKind

type IntegrationActor =
    { Kind: ActorKind
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

/// The `followup.create` payload as a caller supplies it.
type FollowUpCreate =
    { Title: string
      Reason: string
      RequestedAction: RequestedAction
      Priority: Priority
      ReviewAfter: Instant option
      DueAt: Instant option
      Tags: string list
      /// The contract's free-form `context` object, as JSON text. Vigila does
      /// not interpret it, and does not discard it.
      Context: string option }

type CreateRequest =
    { Envelope: ExecutionEnvelope
      FollowUp: FollowUpCreate }

/// The payload after Vigila's validation and normalisation. Two requests that
/// normalise to the same value ask for the same logical follow-up, which is
/// what replay compares.
type NormalisedFollowUp =
    { Title: Title
      Reason: string
      RequestedAction: RequestedAction
      Priority: Priority
      ReviewAfter: Instant option
      DueAt: Instant option
      Tags: TagSet
      Context: string option }

/// The durable record of one accepted `followup.create` operation.
///
/// `Item` is the item as created. Returning it on replay is the "original
/// successful result" VIG-AGT-012 asks for; the item's current state is
/// Vigila's to report through its ordinary read path.
[<NoComparison>]
type FollowUpRecord =
    { Envelope: ExecutionEnvelope
      FollowUp: NormalisedFollowUp
      Item: Item }

/// Why a request was refused before anything was written.
type RefusalCode =
    | ValidationFailed
    | SchemaUnsupported

type Refusal =
    { Code: RefusalCode
      Field: string
      Message: string }

/// What the ledger reports when asked to record an operation.
[<NoComparison>]
type LedgerOutcome =
    /// This call claimed the operation id and persisted the record and item.
    | Recorded
    /// The operation id was already claimed; here is what was recorded.
    | AlreadyRecorded of FollowUpRecord

/// A typed persistence failure. `Code` is a VIG-AGT-050 code.
type LedgerFailure =
    { Code: string
      Retryable: bool
      Detail: string }

exception LedgerFailed of LedgerFailure

/// The durable idempotency port. Tier 4 implements it.
///
/// `Record` MUST claim the operation id atomically: of any number of
/// concurrent calls with one operation id, exactly one may observe `Recorded`,
/// and every other observes `AlreadyRecorded` with the winner's record. That
/// is what makes idempotency concurrency-safe rather than merely sequential,
/// and it is why the port has no separate find-then-record pair.
type FollowUpLedger =
    abstract Record: FollowUpRecord -> Result<LedgerOutcome, LedgerFailure>

/// The structured result of one invocation.
[<NoComparison>]
type CreateOutcome =
    /// The follow-up was created and persisted by this invocation.
    | Created of FollowUpRecord
    /// The operation id was already processed with the same request; nothing
    /// new was created.
    | Replayed of FollowUpRecord
    /// The operation id was already processed with a different request.
    | Conflicted of existing: FollowUpRecord
    /// The request was invalid or unsupported; nothing was written.
    | Rejected of Refusal list
    /// The outcome is not known to be successful. Never reported as success
    /// (VIG-AGT-015); replaying the same operation id is safe.
    | Failed of Fault

[<RequireQualifiedAccess>]
module KnownValue =

    let toOption =
        function
        | Known value -> Some value
        | Unknown
        | NotApplicable -> None

[<RequireQualifiedAccess>]
module CreateOutcome =

    /// The stable machine-readable code (VIG-AGT-050).
    let code =
        function
        | Created _ -> "Created"
        | Replayed _ -> "OperationAlreadyProcessed"
        | Conflicted _ -> "Conflict"
        | Rejected refusals when refusals |> List.exists (fun r -> r.Code = SchemaUnsupported) ->
            "SchemaUnsupported"
        | Rejected _ -> "ValidationFailed"
        | Failed _ -> "PersistenceFailed"

    /// Whether the logical follow-up is known to exist durably.
    let isSuccess =
        function
        | Created _
        | Replayed _ -> true
        | Conflicted _
        | Rejected _
        | Failed _ -> false

// ---------------------------------------------------------------------------
// Validation. Every failure is reported, not only the first, so a caller
// fixing input does not have to iterate (VIG-DOM-047).
// ---------------------------------------------------------------------------

let private refuse field message =
    [ { Code = ValidationFailed
        Field = field
        Message = message } ]

let private required field (value: string | null) =
    match value with
    | Null -> refuse field $"%s{field} is required."
    | NonNull text when String.IsNullOrWhiteSpace text -> refuse field $"%s{field} is required."
    | NonNull _ -> []

let private knownValue field =
    function
    | Known value when String.IsNullOrWhiteSpace value ->
        refuse field $"%s{field} is marked known but has no value."
    | Known _
    | Unknown
    | NotApplicable -> []

let private envelopeRefusals (envelope: ExecutionEnvelope) =
    let actor = envelope.Actor

    let source =
        envelope.Source
        |> Option.map (fun s ->
            [ knownValue "source.repository" s.Repository
              knownValue "source.branch" s.Branch
              knownValue "source.commit" s.Commit
              knownValue "source.workItem" s.WorkItem ])
        |> Option.defaultValue []

    [ required "operationId" envelope.OperationId
      required "correlationId" envelope.CorrelationId
      knownValue "actor.provider" actor.Provider
      knownValue "actor.identity" actor.Identity
      knownValue "actor.runId" actor.RunId
      knownValue "actor.sessionId" actor.SessionId ]
    @ source
    |> List.concat

let private titleResult (text: string | null) =
    match Title.create text with
    | Error message -> Error(refuse "title" message)
    | Ok title when (Title.value title).Length > MaxContractTitleLength ->
        Error(
            refuse
                "title"
                $"A followup.create title may be at most %d{MaxContractTitleLength} characters; got %d{(Title.value title).Length}."
        )
    | Ok title -> Ok title

let private reasonResult (text: string | null) =
    match text with
    | Null -> Error(refuse "reason" "reason is required.")
    | NonNull reason when String.IsNullOrWhiteSpace reason -> Error(refuse "reason" "reason is required.")
    | NonNull reason when reason.Length > Item.MaxDescriptionLength ->
        Error(
            refuse
                "reason"
                $"reason may be at most %d{Item.MaxDescriptionLength} characters; got %d{reason.Length}."
        )
    | NonNull reason -> Ok reason

let private tagsResult (tags: string list) =
    let duplicates =
        tags
        |> List.countBy id
        |> List.filter (fun (_, count) -> count > 1)
        |> List.map (fun (tag, _) -> $"tag '%s{tag}' appears more than once; tags must be unique.")

    match duplicates, TagSet.ofStrings tags with
    | [], Ok set -> Ok set
    | _, Ok _ -> Error(duplicates |> List.collect (refuse "tags"))
    | _, Error failures -> Error((duplicates @ failures) |> List.collect (refuse "tags"))

let private errors =
    function
    | Ok _ -> []
    | Error e -> e

/// Validates and normalises a request without touching any state.
let validate (request: CreateRequest) : Result<NormalisedFollowUp, Refusal list> =
    let followUp = request.FollowUp
    let title = titleResult followUp.Title
    let reason = reasonResult followUp.Reason
    let tags = tagsResult followUp.Tags

    match envelopeRefusals request.Envelope, title, reason, tags with
    | [], Ok title, Ok reason, Ok tags ->
        Ok
            { Title = title
              Reason = reason
              RequestedAction = followUp.RequestedAction
              Priority = followUp.Priority
              ReviewAfter = followUp.ReviewAfter
              DueAt = followUp.DueAt
              Tags = tags
              Context = followUp.Context }
    | envelope, _, _, _ -> Error(envelope @ errors title @ errors reason @ errors tags)

// ---------------------------------------------------------------------------
// Translation into Vigila semantics. Pure apart from the clock and the fresh
// ItemId that Item.create already owns.
// ---------------------------------------------------------------------------

let private actorKindName =
    function
    | AgentKind -> "agent"
    | HumanKind -> "human"
    | AutomationKind -> "automation"
    | SystemKind -> "system"

/// An Echelon `system` actor is another application invoking Vigila, which is
/// exactly what Vigila's ActorType.Integration names.
let private actorType =
    function
    | AgentKind -> ActorType.Agent
    | HumanKind -> ActorType.Human
    | AutomationKind -> ActorType.AutomatedProcess
    | SystemKind -> ActorType.Integration

/// Who, as Vigila's Actor records it. The identity is qualified by its provider
/// when both are known. When the identity is unknown the name says so rather
/// than inventing one; the rest of the provenance is in the operation record.
let actorName (actor: IntegrationActor) =
    match KnownValue.toOption actor.Identity, KnownValue.toOption actor.Provider with
    | Some identity, Some provider -> $"%s{provider}:%s{identity}"
    | Some identity, None -> identity
    | None, _ -> $"unknown %s{actorKindName actor.Kind}"

let private actorOf (actor: IntegrationActor) =
    { Type = actorType actor.Kind
      Name = actorName actor }

let private reference kind value =
    KnownValue.toOption value
    |> Option.map (fun v ->
        { Type = $"echelon.source.%s{kind}"
          DisplayName = v
          ExternalId = Some v
          Url = None })

/// One reference per known source value, plus the operation that created the
/// item so the item leads back to its full provenance.
let private sourcesOf (envelope: ExecutionEnvelope) =
    let operation =
        { Type = "echelon.operation"
          DisplayName = Capability
          ExternalId = Some envelope.OperationId
          Url = None }

    let known =
        envelope.Source
        |> Option.map (fun s ->
            [ reference "repository" s.Repository
              reference "branch" s.Branch
              reference "commit" s.Commit
              reference "work-item" s.WorkItem ]
            |> List.choose id)
        |> Option.defaultValue []

    operation :: known

let requestedActionText =
    function
    | Review -> "Review"
    | Decide -> "Decide"
    | Approve -> "Approve"
    | ProvideInformation -> "Provide information"
    | Investigate -> "Investigate"
    | Other -> "Follow up"

/// Explicit priority mapping. Vigila has no priority scale; high and urgent
/// are flagged Important (VIG-DOM-038), and the exact value survives in the
/// operation record.
let isImportant =
    function
    | High
    | Urgent -> true
    | Low
    | Normal -> false

let private itemFor clock (envelope: ExecutionEnvelope) (followUp: NormalisedFollowUp) =
    let initial =
        Item.create clock (actorOf envelope.Actor) CreatedVia.Integration followUp.Title

    { initial with
        Kind = ItemKind.FollowUp
        Description = Some followUp.Reason
        NextAction = Some(requestedActionText followUp.RequestedAction)
        Due = followUp.DueAt |> Option.map WhenValue.AtInstant
        FollowUp = followUp.ReviewAfter |> Option.map WhenValue.AtInstant
        Sources = sourcesOf envelope
        Tags = followUp.Tags
        Important = isImportant followUp.Priority
        NeedsReview = envelope.Actor.Kind <> HumanKind }

// ---------------------------------------------------------------------------
// The boundary.
// ---------------------------------------------------------------------------

let private ledgerFault aegis scope (failure: LedgerFailure) =
    Aegis.faultOf
        aegis
        scope
        (FaultCode $"VIGILA.INTEGRATION.%s{failure.Code.ToUpperInvariant()}")
        IntegrationFailure
        FaultSeverity.Error
        OperationOnly
        (if failure.Retryable then Transient else Persistent)
        Continue
        "Vigila could not confirm the follow-up was saved. Retrying with the same operation id is safe."
        (LedgerFailed failure)

let private unexpectedFault aegis scope (ex: exn) =
    Aegis.faultOf
        aegis
        scope
        (FaultCode "VIGILA.INTEGRATION.UNEXPECTED")
        IntegrationFailure
        FaultSeverity.Error
        OperationOnly
        UnknownPersistence
        Continue
        "Vigila could not confirm the follow-up was saved. Retrying with the same operation id is safe."
        ex

/// Accepts one `followup.create` request.
///
/// Validation happens before the ledger is touched, so a refused request
/// leaves no partial state. The ledger's atomic claim decides between created
/// and replayed; a replay whose request differs from the recorded one is a
/// conflict rather than a silent success. Unexpected operational failure is
/// captured by Aegis and reported as `Failed`, never as success.
let create (aegis: AegisConfig) (clock: Clock) (ledger: FollowUpLedger) (request: CreateRequest) =
    match validate request with
    | Error refusals -> Rejected refusals
    | Ok followUp ->
        let record =
            { Envelope = request.Envelope
              FollowUp = followUp
              Item = itemFor clock request.Envelope followUp }

        let scope =
            Aegis.scope
                aegis
                "Vigila.Application.Integration.create"
                (Map.ofList
                    [ "capability", ContextValue.Public Capability
                      "operationId", ContextValue.Internal request.Envelope.OperationId
                      "correlationId", ContextValue.Internal request.Envelope.CorrelationId ])

        match Aegis.capture aegis scope (unexpectedFault aegis) (fun () -> ledger.Record record) with
        | Ok(Ok Recorded) -> Created record
        | Ok(Ok(AlreadyRecorded existing)) when existing.FollowUp = followUp -> Replayed existing
        | Ok(Ok(AlreadyRecorded existing)) -> Conflicted existing
        | Ok(Error failure) -> Failed(ledgerFault aegis scope failure)
        | Error fault -> Failed fault
