/// Tier 1 - agent identity and provenance, as the Echelon contract defines it.
///
/// Praxis owns the contract: the actor (RQ-ROS-2026-A001), execution-keyed
/// contributions (RQ-ROS-2026-A004), and the versioned interchange record
/// `praxis.provenance-record` (RQ-ROS-2026-A013..A015). Vigila takes no
/// dependency on Praxis (VIG-AGT-037). This module is Vigila's own small
/// implementation of the parts Vigila needs, kept to the same JSON, and tested
/// against the vendored Praxis conformance fixtures.
///
/// A record is held as the `Verbatim` value it arrived as. It is read into the
/// typed view below only to validate it and to decide what may be appended.
/// Every change is made to the verbatim value itself, so fields this version
/// does not model survive at every level (VIG-PER-025).
///
/// Identity here is self-reported provenance. It is not authentication,
/// authorization, or evidence (VIG-DOM-050).
///
/// Requirements: VIG-DOM-050, VIG-DOM-051, VIG-DOM-052, VIG-DOM-053,
/// VIG-DOM-054, VIG-DOM-055, VIG-AGT-036, VIG-AGT-037.
module Vigila.Semantic.Provenance

open System
open System.Text.RegularExpressions
open Vigila.Semantic.Carried
open Vigila.Semantic.Time
open Vigila.Semantic.Actors

// ---------------------------------------------------------------------------
// Executions
// ---------------------------------------------------------------------------

/// The run an operation is recorded under (VIG-DOM-052).
[<RequireQualifiedAccess>]
type ContributionKey =
    /// `EXE-...`: a propagated Praxis execution, or a system's own namespaced
    /// run `EXE-<system>.<run>`.
    | Execution of string
    /// `CTB-...`: a human or automation acting outside any run.
    | Outside of string

[<RequireQualifiedAccess>]
module ContributionKey =

    let private executionPattern = Regex("^EXE-[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)
    let private outsidePattern = Regex("^CTB-[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)
    let private systemPattern = Regex("^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant)
    let private runPattern = Regex("^[A-Za-z0-9_-][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)
    let private foreignPattern = Regex("^EXE-[a-z][a-z0-9-]*\.", RegexOptions.CultureInvariant)

    let value key =
        match key with
        | ContributionKey.Execution text
        | ContributionKey.Outside text -> text

    let parse (text: string) =
        if executionPattern.IsMatch text then Ok(ContributionKey.Execution text)
        elif outsidePattern.IsMatch text then Ok(ContributionKey.Outside text)
        else Error $"'%s{text}' is not an execution (EXE-...) or contribution (CTB-...) key."

    /// A run of `system` that no Praxis execution was propagated to
    /// (RQ-ROS-2026-A014). Namespaced, so it can never be mistaken for, or
    /// impersonate, a Praxis `EXE-<timestamp>-<random>` execution.
    let foreignExecution (system: string) (run: string) =
        if not (systemPattern.IsMatch system) then
            Error $"System '%s{system}' must match ^[a-z][a-z0-9-]*$."
        elif not (runPattern.IsMatch run) then
            Error $"Run '%s{run}' must match ^[A-Za-z0-9_-][A-Za-z0-9._-]*$."
        else
            Ok(ContributionKey.Execution $"EXE-%s{system}.%s{run}")

    /// Whether an execution key was minted by a system other than Praxis.
    let isForeign key =
        match key with
        | ContributionKey.Execution text -> foreignPattern.IsMatch text
        | ContributionKey.Outside _ -> false

/// Vigila's own system name in execution keys and subjects.
[<Literal>]
let SystemName = "vigila"

/// Who performed an operation, and in which run (VIG-DOM-052).
///
/// An agent is always keyed by an execution: a `CTB-...` key is refused for
/// one. An unknown execution is `None`, which says nothing about the run --
/// Vigila never infers one.
type Attribution =
    { Actor: Actor
      Execution: ContributionKey option }

[<RequireQualifiedAccess>]
module Attribution =

    let create (actor: Actor) (execution: ContributionKey option) =
        match Actor.problems actor, actor.Type, execution with
        | problem :: _, _, _ -> Error problem
        | [], Agent, Some(ContributionKey.Outside key) ->
            Error $"An agent acts within an execution; '%s{key}' is a contribution key, not an execution."
        | [], _, _ -> Ok { Actor = actor; Execution = execution }

// ---------------------------------------------------------------------------
// Operations
// ---------------------------------------------------------------------------

/// What a contribution did. The Praxis operations, the interchange extension
/// operations (RQ-ROS-2026-A015), and any other namespaced `x-...` extension.
/// Named cases exist so callers use these words rather than synonyms
/// (VIG-DOM-055).
[<RequireQualifiedAccess>]
type ContributionOperation =
    | Created
    | Modified
    | Reviewed
    | Approved
    | Superseded
    | Migrated
    /// `x-handled`: acted on a follow-up.
    | Handled
    /// `x-resolved`: resolved it.
    | Resolved
    /// `x-remediated`: changed the affected artifact to address a finding.
    | Remediated
    /// `x-validated`: verified a resolution or remediation.
    | Validated
    /// `x-dismissed`: closed without action.
    | Dismissed
    | Extension of string

[<RequireQualifiedAccess>]
module ContributionOperation =

    let private extensionPattern = Regex("^x-[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant)

    let code operation =
        match operation with
        | ContributionOperation.Created -> "created"
        | ContributionOperation.Modified -> "modified"
        | ContributionOperation.Reviewed -> "reviewed"
        | ContributionOperation.Approved -> "approved"
        | ContributionOperation.Superseded -> "superseded"
        | ContributionOperation.Migrated -> "migrated"
        | ContributionOperation.Handled -> "x-handled"
        | ContributionOperation.Resolved -> "x-resolved"
        | ContributionOperation.Remediated -> "x-remediated"
        | ContributionOperation.Validated -> "x-validated"
        | ContributionOperation.Dismissed -> "x-dismissed"
        | ContributionOperation.Extension text -> text

    let tryParse (text: string) =
        match text with
        | "created" -> Some ContributionOperation.Created
        | "modified" -> Some ContributionOperation.Modified
        | "reviewed" -> Some ContributionOperation.Reviewed
        | "approved" -> Some ContributionOperation.Approved
        | "superseded" -> Some ContributionOperation.Superseded
        | "migrated" -> Some ContributionOperation.Migrated
        | "x-handled" -> Some ContributionOperation.Handled
        | "x-resolved" -> Some ContributionOperation.Resolved
        | "x-remediated" -> Some ContributionOperation.Remediated
        | "x-validated" -> Some ContributionOperation.Validated
        | "x-dismissed" -> Some ContributionOperation.Dismissed
        | extension when extensionPattern.IsMatch extension -> Some(ContributionOperation.Extension extension)
        | _ -> None

// ---------------------------------------------------------------------------
// The Praxis actor, and Vigila's mapping onto it (VIG-DOM-051, ADR-0004)
// ---------------------------------------------------------------------------

/// An actor as a provenance record states it. `Provider`, `Model` and
/// `Runtime` are `None` when not applicable (a human) and `Some "unknown"`
/// when applicable but not known.
type PraxisActor =
    { Kind: string
      Id: string
      Provider: string option
      Model: string option
      Runtime: string option }

[<RequireQualifiedAccess>]
module PraxisActor =

    /// The field that keeps the Vigila type distinguishable where the Praxis
    /// kind alone would lose it. Namespaced, so it cannot collide with a
    /// field a later contract version defines, and preserved by every
    /// conforming consumer because unknown actor fields are.
    [<Literal>]
    let VigilaTypeField = "x-vigila-type"

    let private kindPattern =
        Regex("^(agent|human|automation|unknown|x-[a-z0-9][a-z0-9-]*)$", RegexOptions.CultureInvariant)

    let isKind (text: string) = kindPattern.IsMatch text

    /// Vigila type to Praxis kind. `Integration` has no Praxis kind of its own:
    /// it is a deterministic, non-agent process, so it is `automation`, and
    /// the Vigila type travels beside it.
    let kindOf actorType =
        match actorType with
        | Human -> "human"
        | Agent -> "agent"
        | AutomatedProcess
        | Integration -> "automation"

    /// The Vigila type recorded beside the kind, when the kind alone loses it.
    let vigilaTypeOf actorType =
        match actorType with
        | Integration -> Some "integration"
        | Human
        | Agent
        | AutomatedProcess -> None

    /// Praxis kind (and the preserved Vigila type) back to the Vigila type.
    /// `unknown` and `x-...` kinds have no Vigila type; they stay in the record
    /// and are never coerced into one.
    let typeOf (kind: string) (vigilaType: string option) =
        match kind, vigilaType with
        | "human", None -> Some Human
        | "agent", None -> Some Agent
        | "automation", None -> Some AutomatedProcess
        | "automation", Some "integration" -> Some Integration
        | _ -> None

    let ofActor (actor: Actor) =
        { Kind = kindOf actor.Type
          Id = actor.Name
          Provider = actor.Tooling |> Option.map _.Provider
          Model = actor.Tooling |> Option.map _.Model
          Runtime = actor.Tooling |> Option.map _.Runtime }

    /// The canonical JSON form of a Vigila actor in a provenance record:
    /// kind, id, provider, model, runtime, in that order, then the Vigila type
    /// when the kind alone would lose it.
    let valueOf (actor: Actor) =
        let praxis = ofActor actor

        [ yield "kind", Verbatim.String praxis.Kind
          yield "id", Verbatim.String praxis.Id
          for name, value in [ "provider", praxis.Provider; "model", praxis.Model; "runtime", praxis.Runtime ] do
              match value with
              | Some text -> yield name, Verbatim.String text
              | None -> ()
          match vigilaTypeOf actor.Type with
          | Some vigilaType -> yield VigilaTypeField, Verbatim.String vigilaType
          | None -> () ]
        |> Verbatim.Object

    let private known (value: string) =
        value.Trim().Length > 0 && value.Trim() <> Tooling.UnknownValue

    /// Two statements of an actor agree when kind and stable id agree and no
    /// known attribute contradicts the other. "unknown" contradicts nothing.
    let agrees (left: PraxisActor) (right: PraxisActor) =
        let compatible (a: string option) (b: string option) =
            match a, b with
            | Some x, Some y when known x && known y -> x = y
            | _ -> true

        left.Kind = right.Kind
        && (left.Id = right.Id || not (known left.Id) || not (known right.Id))
        && compatible left.Provider right.Provider
        && compatible left.Model right.Model
        && compatible left.Runtime right.Runtime

    let describe (actor: PraxisActor) =
        let detail =
            match [ actor.Provider; actor.Model; actor.Runtime ] |> List.choose id with
            | [] -> ""
            | values -> " (" + String.concat ", " values + ")"

        $"%s{actor.Kind}:%s{actor.Id}%s{detail}"

    /// Structural problems, as (field, message).
    let problems (actor: PraxisActor) =
        [ if actor.Id.Trim().Length = 0 then
              "id", "actor id must not be empty; use 'unknown' when it is not known"
          if not (isKind actor.Kind) then
              "kind", $"invalid actor kind '%s{actor.Kind}'"
          if actor.Kind = "agent" then
              for field, value in [ "provider", actor.Provider; "model", actor.Model; "runtime", actor.Runtime ] do
                  match value with
                  | None -> field, $"agent actor must record %s{field} (use 'unknown' when it is not known)"
                  | Some text when text.Trim().Length = 0 -> field, $"agent actor %s{field} must not be empty"
                  | Some _ -> ()
          for field, value in [ "id", Some actor.Id; "provider", actor.Provider; "model", actor.Model; "runtime", actor.Runtime ] do
              match value with
              | Some text when Credentials.looksLikeCredential text ->
                  field, "value looks like a credential; identity must never carry secrets"
              | _ -> () ]

// ---------------------------------------------------------------------------
// The interchange record: typed view
// ---------------------------------------------------------------------------

/// A structural problem, at a dotted field path within the record.
type ProvenanceProblem = { Field: string; Message: string }

/// The contract's semantic version.
type ContractVersion = { Major: int; Minor: int; Patch: int }

[<RequireQualifiedAccess>]
module ContractVersion =

    let private pattern =
        Regex("^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)

    /// The version Vigila writes.
    let current = { Major = 1; Minor = 0; Patch = 0 }

    let code version = $"%d{version.Major}.%d{version.Minor}.%d{version.Patch}"

    let tryParse (text: string) =
        let matched = pattern.Match text

        if not matched.Success then
            None
        else
            match
                Int32.TryParse matched.Groups[1].Value,
                Int32.TryParse matched.Groups[2].Value,
                Int32.TryParse matched.Groups[3].Value
            with
            | (true, major), (true, minor), (true, patch) -> Some { Major = major; Minor = minor; Patch = patch }
            | _ -> None

    /// Any minor or patch of major version 1 (VIG-AGT-037).
    let isSupported version = version.Major = current.Major

/// One contribution, as a record states it.
type Contribution =
    { Key: string
      Operations: ContributionOperation list
      At: string
      Last: string option
      Actor: PraxisActor
      Reason: string option
      Evidence: string list }

/// A lineage snapshot: the source's own record. One in an unsupported major
/// version is opaque -- preserved, never interpreted.
[<RequireQualifiedAccess>]
type SourceSnapshot =
    | Known of ProvenanceRecord
    | Opaque of version: string

/// The typed view of an interchange record.
and ProvenanceRecord =
    { Version: ContractVersion
      Subject: string option
      Contributions: Contribution list
      DerivedFrom: string list
      Sources: (string * SourceSnapshot) list }

/// What a reader found.
[<RequireQualifiedAccess>]
type Reading =
    /// `contract` and `version` present, supported major.
    | Current of ProvenanceRecord
    /// A bare `{"contributions": ...}` block, read as version 1.0.0.
    | Unversioned of ProvenanceRecord
    /// A well-labelled record in a major version Vigila does not support:
    /// carried verbatim, never interpreted, modified, or extended.
    | Unsupported of version: string

/// One contribution anywhere in a record's lineage, kept under the subject it
/// belongs to, so a source's contributors are never presented as the
/// derivative's.
type ChainLink =
    { Subject: string option
      Depth: int
      Contribution: Contribution }

/// A contribution Vigila is about to record.
type NewContribution =
    { Attribution: Attribution
      Operations: ContributionOperation list
      At: Instant
      Reason: string option
      Evidence: string list }

[<RequireQualifiedAccess>]
module ProvenanceRecord =

    [<Literal>]
    let ContractName = "praxis.provenance-record"

    /// Lineage snapshots nest; a reader follows at most this many levels.
    [<Literal>]
    let MaxSourceDepth = 16

    let private referencePattern = Regex("^\S+$", RegexOptions.CultureInvariant)

    let private timestampPattern =
        Regex("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,9})?Z$", RegexOptions.CultureInvariant)

    let private problem field message = { Field = field; Message = message }

    let private within prefix (items: ProvenanceProblem list) =
        items
        |> List.map (fun item ->
            { item with
                Field = if item.Field = "" then prefix else $"%s{prefix}.%s{item.Field}" })

    /// The subject of an item's record.
    let subjectOf (itemId: Vigila.Semantic.Identifiers.ItemId) =
        $"%s{SystemName}:item/%s{itemId.Value.ToString()}"

    /// An instant in the contract's timestamp form: UTC, milliseconds, `Z`.
    let timestamp (instant: Instant) =
        (Instant.toDateTimeOffset instant)
            .UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Globalization.CultureInfo.InvariantCulture)

    let private isTimestamp (text: string) =
        timestampPattern.IsMatch text
        && fst (
            DateTimeOffset.TryParse(
                text,
                Globalization.CultureInfo.InvariantCulture,
                Globalization.DateTimeStyles.AssumeUniversal
            )
        )

    /// `at` as a comparable instant; an invalid timestamp sorts last so it
    /// never masquerades as the originating contribution.
    let private instantOf (text: string) =
        match
            DateTimeOffset.TryParse(
                text,
                Globalization.CultureInfo.InvariantCulture,
                Globalization.DateTimeStyles.AssumeUniversal
            )
        with
        | true, value -> value
        | _ -> DateTimeOffset.MaxValue

    let private isCreation (contribution: Contribution) =
        contribution.Operations |> List.contains ContributionOperation.Created

    let originator (record: ProvenanceRecord) = record.Contributions |> List.tryFind isCreation

    // --- reading ----------------------------------------------------------

    let private collect (results: Result<'a, ProvenanceProblem list> list) =
        match results |> List.collect (function Error items -> items | Ok _ -> []) with
        | [] -> results |> List.choose (function Ok value -> Some value | Error _ -> None) |> Ok
        | items -> Error items

    let private stringArray field (value: Verbatim option) =
        match value with
        | None -> Ok []
        | Some(Verbatim.Array items) when items |> List.forall (Verbatim.asString >> Option.isSome) ->
            Ok(items |> List.choose Verbatim.asString)
        | Some _ -> Error [ problem field "must be an array of strings" ]

    let private optionalString field (value: Verbatim option) =
        match value with
        | None -> Ok None
        | Some(Verbatim.String text) -> Ok(Some text)
        | Some _ -> Error [ problem field "must be a string" ]

    let private readActor field (value: Verbatim option) =
        match value with
        | None -> Error [ problem field "actor is required" ]
        | Some(Verbatim.Object _ as actor) ->
            match Verbatim.field "kind" actor with
            | Some(Verbatim.String kind) when PraxisActor.isKind kind ->
                let text name =
                    match Verbatim.field name actor with
                    | Some(Verbatim.String value) -> Ok(Some value)
                    | None -> Ok None
                    | Some _ -> Error [ problem $"%s{field}.%s{name}" "must be a string" ]

                match text "id", text "provider", text "model", text "runtime" with
                | Ok id, Ok provider, Ok model, Ok runtime ->
                    Ok
                        { Kind = kind
                          Id = defaultArg id ""
                          Provider = provider
                          Model = model
                          Runtime = runtime }
                | a, b, c, d ->
                    [ a; b; c; d ] |> List.collect (function Error items -> items | Ok _ -> []) |> Error
            | Some(Verbatim.String kind) -> Error [ problem $"%s{field}.kind" $"unknown actor kind '%s{kind}'" ]
            | _ -> Error [ problem $"%s{field}.kind" "actor.kind is required" ]
        | Some _ -> Error [ problem field "actor must be an object" ]

    let private readContribution (key: string) (value: Verbatim) =
        let prefix = $"contributions.%s{key}"

        match value with
        | Verbatim.Object _ ->
            let operations = stringArray $"%s{prefix}.operations" (Verbatim.field "operations" value)
            let evidence = stringArray $"%s{prefix}.evidence" (Verbatim.field "evidence" value)
            let at = optionalString $"%s{prefix}.at" (Verbatim.field "at" value)
            let last = optionalString $"%s{prefix}.last" (Verbatim.field "last" value)
            let reason = optionalString $"%s{prefix}.reason" (Verbatim.field "reason" value)
            let actor = readActor $"%s{prefix}.actor" (Verbatim.field "actor" value)

            match operations, evidence, at, last, reason, actor with
            | Ok texts, Ok evidence, Ok at, Ok last, Ok reason, Ok actor ->
                let unknown =
                    texts
                    |> List.filter (ContributionOperation.tryParse >> Option.isNone)
                    |> List.map (fun text -> problem $"%s{prefix}.operations" $"unknown operation '%s{text}'")

                let duplicates =
                    if List.length (List.distinct texts) <> List.length texts then
                        [ problem $"%s{prefix}.operations" "operations must be unique" ]
                    else
                        []

                match unknown @ duplicates with
                | [] ->
                    Ok
                        { Key = key
                          Operations = texts |> List.choose ContributionOperation.tryParse
                          At = defaultArg at ""
                          Last = last
                          Actor = actor
                          Reason = reason
                          Evidence = evidence }
                | items -> Error items
            | a, b, c, d, e, f ->
                [ Result.map ignore a
                  Result.map ignore b
                  Result.map ignore c
                  Result.map ignore d
                  Result.map ignore e
                  Result.map ignore f ]
                |> List.collect (function Error items -> items | Ok() -> [])
                |> Error
        | _ -> Error [ problem prefix "contribution must be an object" ]

    let rec private readAt (depth: int) (value: Verbatim) : Result<Reading, ProvenanceProblem list> =
        match value with
        | Verbatim.Object _ ->
            let body version =
                let contributions =
                    match Verbatim.field "contributions" value with
                    | Some(Verbatim.Object entries) ->
                        entries |> List.map (fun (key, entry) -> readContribution key entry) |> collect
                    | None -> Error [ problem "contributions" "contributions is required" ]
                    | Some _ ->
                        Error
                            [ problem
                                  "contributions"
                                  "contributions must be an object keyed by execution (EXE-...) or contribution (CTB-...) ID" ]

                let derivedFrom = stringArray "derivedFrom" (Verbatim.field "derivedFrom" value)
                let subject = optionalString "subject" (Verbatim.field "subject" value)

                let sources =
                    match Verbatim.field "sources" value with
                    | None -> Ok []
                    | Some(Verbatim.Object snapshots) when depth >= MaxSourceDepth && not snapshots.IsEmpty ->
                        Error [ problem "sources" $"lineage snapshots nest deeper than %d{MaxSourceDepth} levels" ]
                    | Some(Verbatim.Object snapshots) ->
                        snapshots
                        |> List.map (fun (reference, snapshot) ->
                            match readAt (depth + 1) snapshot with
                            | Ok(Reading.Current source)
                            | Ok(Reading.Unversioned source) -> Ok(reference, SourceSnapshot.Known source)
                            | Ok(Reading.Unsupported version) -> Ok(reference, SourceSnapshot.Opaque version)
                            | Error items -> Error(within $"sources.%s{reference}" items))
                        |> collect
                    | Some _ -> Error [ problem "sources" "sources must be an object keyed by lineage reference" ]

                match contributions, derivedFrom, subject, sources with
                | Ok contributions, Ok derivedFrom, Ok subject, Ok sources ->
                    Ok
                        { Version = version
                          Subject = subject
                          Contributions = contributions
                          DerivedFrom = derivedFrom
                          Sources = sources }
                | a, b, c, d ->
                    [ Result.map ignore a; Result.map ignore b; Result.map ignore c; Result.map ignore d ]
                    |> List.collect (function Error items -> items | Ok() -> [])
                    |> Error

            match Verbatim.field "contract" value, Verbatim.field "version" value with
            | None, None -> body ContractVersion.current |> Result.map Reading.Unversioned
            | contract, _ when (contract |> Option.bind Verbatim.asString) <> Some ContractName ->
                Error [ problem "contract" $"contract must be '%s{ContractName}'" ]
            | _, version ->
                match version |> Option.bind Verbatim.asString |> Option.bind ContractVersion.tryParse with
                | None -> Error [ problem "version" "version must be a semantic version (MAJOR.MINOR.PATCH)" ]
                | Some parsed when not (ContractVersion.isSupported parsed) ->
                    Ok(Reading.Unsupported(ContractVersion.code parsed))
                | Some parsed -> body parsed |> Result.map Reading.Current
        | _ -> Error [ problem "" "a provenance record must be a JSON object" ]

    /// Reads a record's structure. See `validate` for the rules.
    let read value = readAt 0 value

    // --- validation -------------------------------------------------------

    let private contributionProblems (contribution: Contribution) =
        [ if
              not (
                  Regex.IsMatch(contribution.Key, "^(EXE|CTB)-[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)
              )
          then
              "key",
              $"contribution key '%s{contribution.Key}' must be an execution ID (EXE-...) or a contribution ID (CTB-...)"
          if contribution.Operations.IsEmpty then
              "operations", "contribution must record at least one operation"
          if not (isTimestamp contribution.At) then
              "at", $"'%s{contribution.At}' is not an ISO-8601 UTC timestamp (yyyy-MM-ddTHH:mm:ss[.fff]Z)"
          match contribution.Last with
          | Some last when not (isTimestamp last) -> "last", $"'%s{last}' is not an ISO-8601 UTC timestamp"
          | Some last when instantOf last < instantOf contribution.At -> "last", "last must not precede at"
          | _ -> ()
          yield! PraxisActor.problems contribution.Actor |> List.map (fun (field, message) -> $"actor.%s{field}", message)
          if contribution.Actor.Kind = "agent" && not (contribution.Key.StartsWith("EXE-", StringComparison.Ordinal)) then
              "key", "an agent contribution must be keyed by the execution (EXE-...) that produced it"
          if contribution.Evidence |> List.exists (fun item -> item.Trim().Length = 0) then
              "evidence", "evidence references must not be empty"
          if contribution.Reason |> Option.exists Credentials.looksLikeCredential then
              "reason", "reason looks like it contains a credential; provenance must never carry secrets"
          if contribution.Evidence |> List.exists Credentials.looksLikeCredential then
              "evidence", "an evidence reference looks like a credential; provenance must never carry secrets" ]
        |> List.map (fun (field, message) -> problem $"contributions.%s{contribution.Key}.%s{field}" message)

    let rec private problemsAt (depth: int) (record: ProvenanceRecord) : ProvenanceProblem list =
        let perContribution = record.Contributions |> List.collect contributionProblems

        let creationProblems =
            match record.Contributions |> List.filter isCreation with
            | [] -> []
            | [ creation ] ->
                record.Contributions
                |> List.filter (fun item -> instantOf item.At < instantOf creation.At)
                |> List.map (fun item ->
                    problem
                        $"contributions.%s{item.Key}.at"
                        $"contribution precedes the recorded creation (%s{creation.Key} at %s{creation.At})")
            | many ->
                [ problem
                      "contributions"
                      ("more than one contribution claims 'created': "
                       + (many |> List.map _.Key |> String.concat ", ")) ]

        let referenceProblems name (values: string list) =
            values
            |> List.filter (referencePattern.IsMatch >> not)
            |> List.map (fun value -> problem name $"reference '%s{value}' must be a non-empty token without whitespace")

        let credentialProblems =
            [ yield! record.Subject |> Option.toList |> List.map (fun value -> "subject", value)
              yield! record.DerivedFrom |> List.map (fun value -> "derivedFrom", value) ]
            |> List.filter (snd >> Credentials.looksLikeCredential)
            |> List.map (fun (field, _) ->
                problem field "value looks like a credential; provenance must never carry secrets")

        let lineageProblems =
            [ match record.Subject with
              | Some subject when List.contains subject record.DerivedFrom ->
                  problem "derivedFrom" $"'%s{subject}' cannot be derived from itself"
              | _ -> ()
              if List.length (List.distinct record.DerivedFrom) <> List.length record.DerivedFrom then
                  problem "derivedFrom" "lineage references must be unique"
              for reference, snapshot in record.Sources do
                  if not (List.contains reference record.DerivedFrom) then
                      problem $"sources.%s{reference}" "a lineage snapshot must name a reference listed in derivedFrom"

                  match snapshot with
                  | SourceSnapshot.Known source when source.Subject.IsSome && source.Subject <> Some reference ->
                      problem
                          $"sources.%s{reference}.subject"
                          $"the snapshot describes '%s{source.Subject.Value}', not '%s{reference}'; a lineage snapshot must be the named source's own provenance"
                  | _ -> () ]

        let sourceProblems =
            record.Sources
            |> List.collect (fun (reference, snapshot) ->
                match snapshot with
                | SourceSnapshot.Opaque _ -> []
                | SourceSnapshot.Known _ when depth >= MaxSourceDepth ->
                    [ problem $"sources.%s{reference}" $"lineage snapshots nest deeper than %d{MaxSourceDepth} levels" ]
                | SourceSnapshot.Known source -> problemsAt (depth + 1) source |> within $"sources.%s{reference}")

        perContribution
        @ creationProblems
        @ referenceProblems "subject" (Option.toList record.Subject)
        @ referenceProblems "derivedFrom" record.DerivedFrom
        @ credentialProblems
        @ lineageProblems
        @ sourceProblems

    /// Structural problems of a record and, recursively, of its lineage
    /// snapshots. Empty means well-formed; it says nothing about whether the
    /// recorded identities are true.
    let problems record = problemsAt 0 record

    /// Reads and applies every structural rule. An unsupported major version
    /// is not an error: the reading says so, and the record is carried as is.
    let validate value =
        match read value with
        | Ok(Reading.Current record)
        | Ok(Reading.Unversioned record) as reading ->
            match problems record with
            | [] -> reading
            | items -> Error items
        | other -> other

    // --- lineage ----------------------------------------------------------

    /// Every contribution in the record and its lineage snapshots, the
    /// record's own first, each under the subject it belongs to.
    let chain (record: ProvenanceRecord) =
        let rec walk depth (current: ProvenanceRecord) =
            let own =
                current.Contributions
                |> List.map (fun contribution ->
                    { Subject = current.Subject
                      Depth = depth
                      Contribution = contribution })

            let lineage =
                if depth >= MaxSourceDepth then
                    []
                else
                    current.Sources
                    |> List.collect (fun (reference, snapshot) ->
                        match snapshot with
                        | SourceSnapshot.Known source ->
                            walk (depth + 1) { source with Subject = source.Subject |> Option.orElse (Some reference) }
                        | SourceSnapshot.Opaque _ -> [])

            own @ lineage

        walk 0 record

    // --- successor check (RQ-ROS-2026-A015) -------------------------------

    let private modelProblems (before: ProvenanceRecord) (after: ProvenanceRecord) =
        let afterByKey = after.Contributions |> List.map (fun item -> item.Key, item) |> Map.ofList

        let contributions =
            before.Contributions
            |> List.collect (fun previous ->
                let field name = $"contributions.%s{previous.Key}%s{name}"

                match afterByKey |> Map.tryFind previous.Key with
                | None -> [ problem (field "") "contribution was removed; provenance history is append-only" ]
                | Some current ->
                    [ if current.Actor <> previous.Actor then
                          problem
                              (field ".actor")
                              $"actor changed from %s{PraxisActor.describe previous.Actor} to %s{PraxisActor.describe current.Actor}"
                      if current.At <> previous.At then
                          problem (field ".at") "the time of the first recorded operation changed"
                      for operation in previous.Operations do
                          if not (List.contains operation current.Operations) then
                              problem
                                  (field ".operations")
                                  $"operation '%s{ContributionOperation.code operation}' was removed"
                      for item in previous.Evidence do
                          if not (List.contains item current.Evidence) then
                              problem (field ".evidence") $"evidence '%s{item}' was removed"
                      match previous.Reason with
                      | Some reason when current.Reason <> Some reason -> problem (field ".reason") "reason was rewritten"
                      | _ -> () ])

        let origin =
            match originator before, originator after with
            | Some previous, Some current when previous.Key <> current.Key ->
                [ problem "contributions" $"originator changed from %s{previous.Key} to %s{current.Key}" ]
            | _ -> []

        let lineage =
            [ for reference in before.DerivedFrom do
                  if not (List.contains reference after.DerivedFrom) then
                      problem "derivedFrom" $"lineage reference '%s{reference}' was removed"
              for reference, _ in before.Sources do
                  if not (after.Sources |> List.exists (fst >> (=) reference)) then
                      problem $"sources.%s{reference}" "lineage snapshot was removed"
              if before.Subject.IsSome && after.Subject <> before.Subject then
                  problem "subject" "subject changed; a different subject needs its own record that derives from this one"
              if after.Version.Major <> before.Version.Major then
                  problem "version" "major version changed in place"
              elif compare (after.Version.Minor, after.Version.Patch) (before.Version.Minor, before.Version.Patch) < 0 then
                  problem
                      "version"
                      $"version was lowered from %s{ContractVersion.code before.Version} to %s{ContractVersion.code after.Version}" ]

        contributions @ origin @ lineage

    /// Everything `before` carried -- including fields Vigila does not model,
    /// on the record, each contribution, and each actor -- is still present
    /// and unchanged in `after`, except the fields a contribution may extend.
    let private preservationProblems (before: Verbatim) (after: Verbatim) =
        let members value =
            match value with
            | Verbatim.Object items -> items
            | _ -> []

        let modelled = set [ "contract"; "version"; "contributions"; "derivedFrom"; "sources" ]

        let topLevel =
            members before
            |> List.filter (fst >> modelled.Contains >> not)
            |> List.filter (fun (key, value) ->
                match Verbatim.field key after with
                | Some other -> not (Verbatim.equivalent value other)
                | None -> true)
            |> List.map (fun (key, _) ->
                problem key "field was removed or changed; fields a consumer does not model must be preserved")

        let extendable = set [ "operations"; "evidence"; "last"; "reason" ]

        let contributions =
            match Verbatim.field "contributions" before, Verbatim.field "contributions" after with
            | Some previous, Some current ->
                members previous
                |> List.collect (fun (key, entry) ->
                    match Verbatim.field key current with
                    | Some successor ->
                        members entry
                        |> List.filter (fst >> extendable.Contains >> not)
                        |> List.filter (fun (name, value) ->
                            match Verbatim.field name successor with
                            | Some other -> not (Verbatim.equivalent value other)
                            | None -> true)
                        |> List.map (fun (name, _) ->
                            problem
                                $"contributions.%s{key}.%s{name}"
                                "field was removed or changed; another contributor's entry must be preserved verbatim")
                    | None -> [])
            | _ -> []

        let snapshots =
            match Verbatim.field "sources" before, Verbatim.field "sources" after with
            | Some previous, Some current ->
                members previous
                |> List.filter (fun (key, value) ->
                    match Verbatim.field key current with
                    | Some other -> not (Verbatim.equivalent value other)
                    | None -> false)
                |> List.map (fun (key, _) -> problem $"sources.%s{key}" "lineage snapshot must be carried verbatim")
            | _ -> []

        topLevel @ contributions @ snapshots

    /// Whether `after` is a non-destructive successor of `before`: nothing
    /// removed, no actor overwritten, no history replaced, no execution lost,
    /// unknown fields kept, and an unsupported record carried unchanged.
    let successorProblems (before: Verbatim) (after: Verbatim) =
        match read before, read after with
        | Ok(Reading.Unsupported _), _ ->
            if Verbatim.equivalent before after then
                []
            else
                [ problem "" "a record in an unsupported major version must be carried verbatim" ]
        | Error _, _ -> [ problem "" "the previous record is malformed; refusing to judge a successor of it" ]
        | _, Error items -> items
        | Ok(Reading.Current previous), Ok(Reading.Current current)
        | Ok(Reading.Current previous), Ok(Reading.Unversioned current)
        | Ok(Reading.Unversioned previous), Ok(Reading.Current current)
        | Ok(Reading.Unversioned previous), Ok(Reading.Unversioned current) ->
            modelProblems previous current @ preservationProblems before after
        | Ok _, Ok(Reading.Unsupported _) ->
            [ problem "version" "a supported record was replaced by an unsupported major version in place" ]

    // --- writing ----------------------------------------------------------

    let private actorOf (contribution: NewContribution) = PraxisActor.ofActor contribution.Attribution.Actor

    let private keyOf (contribution: NewContribution) =
        match contribution.Attribution.Execution with
        | Some key -> Ok(ContributionKey.value key)
        | None -> Error [ problem "contributions" "a contribution needs the execution (EXE-...) or contribution (CTB-...) key it was made under" ]

    let private viewOf key (contribution: NewContribution) =
        { Key = key
          Operations = contribution.Operations |> List.distinct
          At = timestamp contribution.At
          Last = None
          Actor = actorOf contribution
          Reason = contribution.Reason
          Evidence = contribution.Evidence |> List.distinct }

    /// The canonical entry: operations, at, last?, actor, reason?, evidence?.
    let private entryValue (actor: Actor) (view: Contribution) =
        [ yield "operations", view.Operations |> List.map ContributionOperation.code |> Verbatim.strings
          yield "at", Verbatim.String view.At
          match view.Last with
          | Some last -> yield "last", Verbatim.String last
          | None -> ()
          yield "actor", PraxisActor.valueOf actor
          match view.Reason with
          | Some reason -> yield "reason", Verbatim.String reason
          | None -> ()
          if not view.Evidence.IsEmpty then
              yield "evidence", Verbatim.strings view.Evidence ]
        |> Verbatim.Object

    let private checkNew key (contribution: NewContribution) =
        match Actor.problems contribution.Attribution.Actor with
        | message :: _ -> Error [ problem "actor" message ]
        | [] ->
            let view = viewOf key contribution

            match contributionProblems view with
            | [] -> Ok view
            | items -> Error items

    let private envelope = [ "contract", Verbatim.String ContractName; "version", Verbatim.String(ContractVersion.code ContractVersion.current) ]

    /// Starts the record of a subject derived from other subjects
    /// (VIG-DOM-054). `creator` authors the new subject; each source is named
    /// in `derivedFrom`, and its record, when held, is carried verbatim in
    /// `sources` -- never merged into the new subject's contributions.
    let derive (subject: string) (creator: NewContribution) (sources: (string * Verbatim option) list) =
        match keyOf creator |> Result.bind (fun key -> checkNew key creator) with
        | Error items -> Error items
        | Ok view when not (isCreation view) ->
            Error [ problem "contributions" "the first contribution to a derived subject must be 'created'" ]
        | Ok view ->
            let references = sources |> List.map fst |> List.distinct
            let snapshots = sources |> List.distinctBy fst |> List.choose (fun (reference, record) -> record |> Option.map (fun r -> reference, r))

            let value =
                [ yield! envelope
                  yield "subject", Verbatim.String subject
                  yield "contributions", Verbatim.Object [ view.Key, entryValue creator.Attribution.Actor view ]
                  if not references.IsEmpty then
                      yield "derivedFrom", Verbatim.strings references
                  if not snapshots.IsEmpty then
                      yield "sources", Verbatim.Object snapshots ]
                |> Verbatim.Object

            validate value |> Result.map (fun _ -> value)

    /// A fresh record for a subject whose provenance starts here.
    let start (subject: string) (creator: NewContribution) = derive subject creator []

    /// Appends one contribution, or extends the contributing execution's own
    /// entry (VIG-DOM-055). Only the touched entry's `operations`, `last`,
    /// `evidence` and `reason` change; every other part of the record is
    /// carried over and then proven unchanged before the result is returned.
    /// An unversioned block gains the envelope. An unsupported major version,
    /// a malformed record, re-attribution, and a second or late `created` are
    /// refused.
    let append (contribution: NewContribution) (raw: Verbatim) =
        match read raw with
        | Error items -> Error items
        | Ok(Reading.Unsupported version) ->
            Error
                [ problem
                      "version"
                      $"version %s{version} is not supported; the record must be carried verbatim, not extended" ]
        | Ok(Reading.Current record)
        | Ok(Reading.Unversioned record) ->
            match problems record with
            | _ :: _ as items -> Error(problem "" "refusing to extend malformed provenance" :: items)
            | [] ->
                match keyOf contribution |> Result.bind (fun key -> checkNew key contribution) with
                | Error items -> Error items
                | Ok view ->
                    let existing = record.Contributions |> List.tryFind (fun item -> item.Key = view.Key)

                    let entry =
                        match existing with
                        | Some previous when
                            not (PraxisActor.agrees previous.Actor view.Actor)
                            || (Verbatim.field "contributions" raw
                                |> Option.bind (Verbatim.field view.Key)
                                |> Option.bind (Verbatim.field "actor")
                                |> Option.bind (Verbatim.stringField PraxisActor.VigilaTypeField))
                               <> PraxisActor.vigilaTypeOf contribution.Attribution.Actor.Type
                            ->
                            Error
                                [ problem
                                      $"contributions.%s{view.Key}"
                                      $"already attributed to %s{PraxisActor.describe previous.Actor}; refusing to re-attribute it to %s{PraxisActor.describe view.Actor}" ]
                        | Some previous ->
                            let operations =
                                previous.Operations
                                @ (view.Operations |> List.filter (fun op -> not (List.contains op previous.Operations)))

                            let evidence =
                                previous.Evidence
                                @ (view.Evidence |> List.filter (fun item -> not (List.contains item previous.Evidence)))

                            let latestSoFar = previous.Last |> Option.defaultValue previous.At

                            let last =
                                if instantOf view.At > instantOf latestSoFar then Some view.At else previous.Last

                            let reason = previous.Reason |> Option.orElse view.Reason

                            let rawEntry =
                                Verbatim.field "contributions" raw
                                |> Option.bind (Verbatim.field view.Key)
                                |> Option.defaultValue (Verbatim.Object [])

                            [ yield "operations", operations |> List.map ContributionOperation.code |> Verbatim.strings
                              match last with
                              | Some value -> yield "last", Verbatim.String value
                              | None -> ()
                              if not evidence.IsEmpty then
                                  yield "evidence", Verbatim.strings evidence
                              match reason with
                              | Some value -> yield "reason", Verbatim.String value
                              | None -> () ]
                            |> List.fold (fun acc (name, value) -> Verbatim.setField name value acc) rawEntry
                            |> Ok
                        | None when isCreation view && (originator record).IsSome ->
                            Error
                                [ problem
                                      $"contributions.%s{view.Key}"
                                      "the subject already has a recorded originator; record this contribution as 'modified'" ]
                        | None when
                            isCreation view
                            && record.Contributions |> List.exists (fun item -> instantOf item.At < instantOf view.At)
                            ->
                            Error
                                [ problem
                                      $"contributions.%s{view.Key}"
                                      "a 'created' contribution cannot follow existing contributions" ]
                        | None -> Ok(entryValue contribution.Attribution.Actor view)

                    match entry with
                    | Error items -> Error items
                    | Ok entry ->
                        let contributions =
                            Verbatim.field "contributions" raw
                            |> Option.defaultValue (Verbatim.Object [])
                            |> Verbatim.setField view.Key entry

                        let withContributions = Verbatim.setField "contributions" contributions raw

                        let result =
                            match withContributions with
                            | Verbatim.Object members when not (Verbatim.has "contract" raw) -> Verbatim.Object(envelope @ members)
                            | other -> other

                        match validate result with
                        | Error items -> Error items
                        | Ok _ ->
                            match successorProblems raw result with
                            | [] -> Ok result
                            | items -> Error(problem "" "the appended record failed its own preservation check" :: items)
