/// Tier 1 - Praxis provenance, carried rather than redefined.
///
/// Praxis owns what an actor, an execution, a contribution and `unknown` mean
/// (DF-ROS-2026-A036, DF-ROS-2026-A037). This module is the typed form of its
/// interchange block, `praxis.provenance/1`, and the pure rules for appending
/// to it, mirroring the reference library `lib/provenance-interchange.mjs`
/// (kemiller2002/praxis@c2657ef, contract revision 1.1). It adds no concept of its own: VIG-PROV-001
/// forbids a second identity model.
///
/// Why typed rather than an opaque JSON string: the append rules (never
/// re-attribute, at most one `created`, nothing precedes it) are domain rules,
/// and Tier 1 is where "what can be true" lives. Why the `Extensions` fields:
/// a supported block must keep every field this version does not model
/// (VIG-PROV-006), and Tier 1 cannot parse JSON (scripts/check-architecture.sh),
/// so such fields travel as opaque JSON text and are written back unchanged.
///
/// Identity here is self-reported provenance. Nothing in Vigila grants, denies
/// or weights anything by it (VIG-PROV-014).
///
/// Requirements: VIG-PROV-001, VIG-PROV-003, VIG-PROV-004, VIG-PROV-005,
/// VIG-PROV-008, VIG-PROV-013, VIG-PROV-015.
module Vigila.Semantic.Provenance

open System
open System.Globalization
open System.Text.RegularExpressions
open Vigila.Semantic.Actors
open Vigila.Semantic.Time

/// The JSON text of a value this version does not model. Opaque in Tier 1:
/// it is carried and written back, never interpreted.
type RawJson =
    | RawJson of string

    member this.Text = let (RawJson text) = this in text

/// Fields this version does not model, in the order they were received.
type Extensions = (string * RawJson) list

/// A Praxis actor kind. `Extension` holds an `x-...` kind verbatim.
[<RequireQualifiedAccess>]
type ActorKind =
    | Agent
    | Human
    | Automation
    | Unknown
    | Extension of string

/// A Praxis contribution operation. `Other` holds a code this version does not
/// know (an `x-...` extension, or vocabulary from a newer minor release), which
/// is tolerated and preserved verbatim.
[<RequireQualifiedAccess>]
type Operation =
    | Created
    | Modified
    | Reviewed
    | Approved
    | Superseded
    | Migrated
    | Discovered
    | Measured
    | Transformed
    | Remediated
    | Validated
    | Resolved
    | Other of string

/// A Praxis actor: `{kind, id, provider?, model?, runtime?}`. Non-humans carry
/// provider/model/runtime, as the literal "unknown" when unknown; nothing is
/// ever guessed.
type ProvenanceActor =
    { Kind: ActorKind
      Id: string
      Provider: string option
      Model: string option
      Runtime: string option
      Extensions: Extensions }

/// One execution's (or one contributor's) part in a record.
type Contribution =
    { Operations: Operation list
      /// ISO-8601 UTC timestamp, kept as received so it round-trips exactly.
      At: string
      Last: string option
      Actor: ProvenanceActor
      Reason: string option
      Evidence: string list option
      Extensions: Extensions }

/// A `praxis.provenance/1` block. `Schema` is `None` for the bare registry
/// projection, which Praxis also reads as major 1. Contributions keep their
/// received order.
type ProvenanceBlock =
    { Schema: string option
      Contributions: (string * Contribution) list
      DerivedFrom: string list option
      Extensions: Extensions }

/// Provenance as a Vigila record holds it.
type ItemProvenance =
    /// A block this version understands and may append to.
    | Recorded of ProvenanceBlock
    /// A block of another major version: carried verbatim, never interpreted,
    /// merged into or appended to (VIG-PROV-006).
    | CarriedVerbatim of schema: string * json: RawJson

/// What kind of key a contribution is filed under (VIG-PROV-004).
type KeyKind =
    | Execution
    | ForeignExecution
    | ContributorKey
    | InvalidKey

[<RequireQualifiedAccess>]
module Operation =

    let code operation =
        match operation with
        | Operation.Created -> "created"
        | Operation.Modified -> "modified"
        | Operation.Reviewed -> "reviewed"
        | Operation.Approved -> "approved"
        | Operation.Superseded -> "superseded"
        | Operation.Migrated -> "migrated"
        | Operation.Discovered -> "discovered"
        | Operation.Measured -> "measured"
        | Operation.Transformed -> "transformed"
        | Operation.Remediated -> "remediated"
        | Operation.Validated -> "validated"
        | Operation.Resolved -> "resolved"
        | Operation.Other text -> text

    /// Parses any code; unknown codes become `Other` and are preserved.
    let ofCode (text: string) =
        match text with
        | "created" -> Operation.Created
        | "modified" -> Operation.Modified
        | "reviewed" -> Operation.Reviewed
        | "approved" -> Operation.Approved
        | "superseded" -> Operation.Superseded
        | "migrated" -> Operation.Migrated
        | "discovered" -> Operation.Discovered
        | "measured" -> Operation.Measured
        | "transformed" -> Operation.Transformed
        | "remediated" -> Operation.Remediated
        | "validated" -> Operation.Validated
        | "resolved" -> Operation.Resolved
        | other -> Operation.Other other

[<RequireQualifiedAccess>]
module ActorKind =

    let private extension = Regex("^x-[a-z0-9][a-z0-9-]*\\z", RegexOptions.CultureInvariant)

    let code kind =
        match kind with
        | ActorKind.Agent -> "agent"
        | ActorKind.Human -> "human"
        | ActorKind.Automation -> "automation"
        | ActorKind.Unknown -> "unknown"
        | ActorKind.Extension text -> text

    /// `None` for anything that is not agent, human, automation, unknown or
    /// a well-formed `x-...` extension.
    let tryParse (text: string) =
        match text with
        | "agent" -> Some ActorKind.Agent
        | "human" -> Some ActorKind.Human
        | "automation" -> Some ActorKind.Automation
        | "unknown" -> Some ActorKind.Unknown
        | other when extension.IsMatch other -> Some(ActorKind.Extension other)
        | _ -> None

[<RequireQualifiedAccess>]
module ProvenanceActor =

    [<Literal>]
    let UnknownValue = "unknown"

    let private known (value: string) = Some value

    let agent id provider model runtime =
        { Kind = ActorKind.Agent
          Id = id
          Provider = known provider
          Model = known model
          Runtime = known runtime
          Extensions = [] }

    /// Humans omit provider, model and runtime.
    let human id =
        { Kind = ActorKind.Human
          Id = id
          Provider = None
          Model = None
          Runtime = None
          Extensions = [] }

    /// A deterministic non-agent process. Its model is "unknown": automation
    /// has none that anyone declared.
    let automation id provider runtime =
        { Kind = ActorKind.Automation
          Id = id
          Provider = known provider
          Model = known UnknownValue
          Runtime = known runtime
          Extensions = [] }

    /// An actor nobody declared. Recorded as such rather than guessed.
    let unknown =
        { Kind = ActorKind.Unknown
          Id = UnknownValue
          Provider = known UnknownValue
          Model = known UnknownValue
          Runtime = known UnknownValue
          Extensions = [] }

    /// Vigila itself, when it transforms a request into an item
    /// (VIG-PROV-012).
    let vigila = automation "echelon/vigila" "echelon" "vigila"

    /// The documented display projection onto Vigila's `{type, name}` actor
    /// (VIG-PROV-015). Lossy by design: the block stays the identity.
    let toLegacy (actor: ProvenanceActor) : Actor =
        let actorType =
            match actor.Kind with
            | ActorKind.Agent -> ActorType.Agent
            | ActorKind.Human -> Human
            | ActorKind.Automation -> AutomatedProcess
            | ActorKind.Unknown
            | ActorKind.Extension _ -> ActorType.Unknown

        let name = actor.Id.Trim()

        { Type = actorType
          Name = if name.Length = 0 then UnknownValue else name }

[<RequireQualifiedAccess>]
module Provenance =

    [<Literal>]
    let SchemaTag = "praxis.provenance/1"

    let private executionKey = Regex("^EXE-[A-Za-z0-9._-]+\\z", RegexOptions.CultureInvariant)
    let private contributorKey = Regex("^CTB-[A-Za-z0-9._-]+\\z", RegexOptions.CultureInvariant)

    let private foreignKey =
        Regex("^EXT-([a-z][a-z0-9-]*)\\.([A-Za-z0-9._-]+)\\z", RegexOptions.CultureInvariant)

    let private operationGrammar = Regex("^[a-z][a-z0-9-]*\\z", RegexOptions.CultureInvariant)
    let private extensionCode = Regex("^x-[a-z0-9][a-z0-9-]*\\z", RegexOptions.CultureInvariant)

    let private timestamp =
        Regex(
            "^([0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2})(\\.([0-9]{1,9}))?Z\\z",
            RegexOptions.CultureInvariant
        )


    /// Credential shapes (RQ-ROS-2026-A017), identical to the reference
    /// library's. A tripwire for accidents, not a secret scanner.
    let private credentialPatterns =
        [ "gh[pousr]_[A-Za-z0-9]{20,}"
          "github_pat_[A-Za-z0-9_]{20,}"
          "sk-[A-Za-z0-9_-]{20,}"
          "AKIA[0-9A-Z]{16}"
          "xox[abprs]-[A-Za-z0-9-]{10,}"
          "-----BEGIN [A-Z ]*PRIVATE KEY-----"
          "(?i)\\bbearer\\s+[A-Za-z0-9._~+/=-]{16,}"
          "eyJ[A-Za-z0-9_-]{10,}\\.[A-Za-z0-9_-]{10,}\\." ]
        |> List.map (fun pattern -> Regex(pattern, RegexOptions.CultureInvariant))

    let isCredentialLike (value: string) =
        credentialPatterns |> List.exists (fun pattern -> pattern.IsMatch value)

    let knownOperationCodes =
        [ "created"; "modified"; "reviewed"; "approved"; "superseded"; "migrated"
          "discovered"; "measured"; "transformed"; "remediated"; "validated"; "resolved" ]

    let empty =
        { Schema = Some SchemaTag
          Contributions = []
          DerivedFrom = None
          Extensions = [] }

    let keyKind (key: string) =
        if executionKey.IsMatch key then Execution
        elif foreignKey.IsMatch key then ForeignExecution
        elif contributorKey.IsMatch key then ContributorKey
        else InvalidKey

    /// `EXT-dokimos.run-7` -> `Some "dokimos"`.
    let foreignSystem (key: string) =
        let m = foreignKey.Match key
        if m.Success then Some m.Groups[1].Value else None

    /// Escapes an id so it can be carried in a key, injectively (contract 1.1,
    /// the reference library's `keyFromEnvelopeV1`): `_` and every character
    /// outside `[A-Za-z0-9.-]` become `_xx` per UTF-8 byte (lower-case hex),
    /// so two different ids can never map to the same key. "op 1" -> "op_201".
    ///
    /// With `keepDots = false`, `.` is escaped too (`_2e`), for a segment that
    /// must not introduce a separator of its own (echelon-registry
    /// REG-PROV-008 `escapeKeySegment`).
    let escapeSegment keepDots (text: string) =
        let builder = Text.StringBuilder()

        for rune in text.EnumerateRunes() do
            let value = rune.Value

            let plain =
                (value >= int 'A' && value <= int 'Z')
                || (value >= int 'a' && value <= int 'z')
                || (value >= int '0' && value <= int '9')
                || (keepDots && value = int '.')
                || value = int '-'

            if plain then
                builder.Append(char value) |> ignore
            else
                let bytes = Array.zeroCreate<byte> rune.Utf8SequenceLength
                rune.EncodeToUtf8(Span<byte>(bytes)) |> ignore

                for b in bytes do
                    builder.Append('_').Append(b.ToString("x2", CultureInfo.InvariantCulture)) |> ignore

        builder.ToString()

    /// `escapeSegment` keeping dots: the Praxis `keyFromEnvelopeV1` escaping.
    let safeSegment (text: string) = escapeSegment true text

    /// `EXT-<system>.<run-id>`, refusing ids it cannot carry.
    let foreignExecutionKey (system: string) (runId: string) =
        let key = $"EXT-%s{system}.%s{runId}"

        if foreignKey.IsMatch key && foreignSystem key = Some system then
            Ok key
        else
            Error $"cannot form a foreign execution key from system '%s{system}' and run '%s{runId}'"

    /// The key for work whose execution is not known: `EXT-op.<operationId>`
    /// (VIG-PROV-004).
    let operationKey (operationId: string) = $"EXT-op.%s{safeSegment operationId}"

    /// Milliseconds since the epoch, or `None` when the text is not a
    /// calendar-valid ISO-8601 UTC timestamp (year 0001-9999, no February 30,
    /// no 24:00). Ordering is at millisecond precision: extra fraction digits
    /// are truncated, never rounded (contract 1.1).
    let private millis (text: string) =
        let m = timestamp.Match text

        if not m.Success then
            None
        else
            match DateTimeOffset.TryParseExact(
                      m.Groups[1].Value + "Z",
                      "yyyy-MM-dd'T'HH:mm:ss'Z'",
                      CultureInfo.InvariantCulture,
                      DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal) with
            | true, value ->
                let fraction = if m.Groups[3].Success then m.Groups[3].Value else ""
                let ms = (fraction + "000").Substring(0, 3) |> int64
                Some(value.ToUnixTimeMilliseconds() + ms)
            | _ -> None

    let isTimestamp text = (millis text).IsSome

    /// Ordering key: an invalid timestamp sorts last, as in the reference.
    let private instant text = millis text |> Option.defaultValue Int64.MaxValue

    /// The contract's timestamp form for a Vigila instant: UTC, milliseconds.
    let timestampOf (value: Instant) =
        (Instant.toDateTimeOffset value)
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)

    let private nonEmpty (text: string) = text.Trim().Length > 0

    let actorProblems prefix (actor: ProvenanceActor) =
        [ match actor.Kind with
          | ActorKind.Extension text when not (extensionCode.IsMatch text) ->
              $"%s{prefix}.kind '%s{text}' is not agent, human, automation, unknown, or x-..."
          | _ -> ()
          if not (nonEmpty actor.Id) then
              $"%s{prefix}.id must not be empty; use 'unknown' when it is not known"
          for field, value in [ "provider", actor.Provider; "model", actor.Model; "runtime", actor.Runtime ] do
              match value with
              | Some text when not (nonEmpty text) -> $"%s{prefix}.%s{field} must be a non-empty string"
              | None when actor.Kind = ActorKind.Agent ->
                  $"%s{prefix}.%s{field} is required for an agent ('unknown' when not known)"
              | _ -> () ]

    /// Same actor: kind, stable id and every known attribute agree; `unknown`
    /// never contradicts.
    let actorsAgree (left: ProvenanceActor) (right: ProvenanceActor) =
        let isKnown (value: string option) =
            match value with
            | Some text -> nonEmpty text && text.Trim() <> ProvenanceActor.UnknownValue
            | None -> false

        let compatible a b = not (isKnown a && isKnown b) || a = b

        left.Kind = right.Kind
        && (left.Id = right.Id || not (isKnown (Some left.Id)) || not (isKnown (Some right.Id)))
        && compatible left.Provider right.Provider
        && compatible left.Model right.Model
        && compatible left.Runtime right.Runtime

    let private stringListProblems field (values: string list option) =
        match values with
        | Some items when items |> List.exists (nonEmpty >> not) -> [ $"%s{field} must be an array of non-empty strings" ]
        | _ -> []

    let contributionProblems (key: string) (entry: Contribution) =
        let prefix = $"contributions.%s{key}"
        let kind = keyKind key
        let codes = entry.Operations |> List.map Operation.code

        [ if kind = InvalidKey then
              $"%s{prefix}: key must be EXE-..., EXT-<system>.<run-id>, or CTB-..."
          if codes.IsEmpty then
              $"%s{prefix}.operations must record at least one operation"
          for code in codes do
              if not (operationGrammar.IsMatch code) then
                  $"%s{prefix}.operations: '%s{code}' is not a valid operation code"
          if (List.distinct codes).Length <> codes.Length then
              $"%s{prefix}.operations must not repeat an operation"
          if not (isTimestamp entry.At) then
              $"%s{prefix}.at must be a calendar-valid ISO-8601 UTC timestamp"
          match entry.Last with
          | Some last when not (isTimestamp last) -> $"%s{prefix}.last must be a calendar-valid ISO-8601 UTC timestamp"
          | Some last when instant last < instant entry.At -> $"%s{prefix}.last must not precede at"
          | _ -> ()
          yield! actorProblems $"%s{prefix}.actor" entry.Actor
          if entry.Actor.Kind = ActorKind.Agent && kind <> Execution && kind <> ForeignExecution then
              $"%s{prefix}: an agent contribution must be keyed by the execution (EXE-... or EXT-...) that produced it"
          yield! stringListProblems $"%s{prefix}.evidence" entry.Evidence ]

    let private creators (contributions: (string * Contribution) list) =
        contributions
        |> List.filter (fun (_, entry) -> entry.Operations |> List.contains Operation.Created)

    let private historyProblems contributions =
        match creators contributions with
        | [] -> []
        | [ creationKey, creation ] ->
            contributions
            |> List.filter (fun (key, entry) -> key <> creationKey && instant entry.At < instant creation.At)
            |> List.map (fun (key, _) -> $"contributions.%s{key} precedes the recorded creation (%s{creationKey})")
        | many ->
            let keys = many |> List.map fst |> String.concat ", "
            [ $"more than one contribution claims 'created': %s{keys}" ]

    /// Why a block is not a valid `praxis.provenance/1` block, if it is not.
    /// Structural JSON checks (types, missing fields) belong to the codec that
    /// built the block; these are the rules that hold for any representation.
    let problems (block: ProvenanceBlock) =
        let schema =
            match block.Schema with
            | Some tag when tag <> SchemaTag -> [ $"schema '%s{tag}' is not %s{SchemaTag}" ]
            | _ -> []

        let entries =
            block.Contributions |> List.collect (fun (key, entry) -> contributionProblems key entry)

        let keys = block.Contributions |> List.map fst

        let duplicates =
            if (List.distinct keys).Length <> keys.Length then [ "contributions must not repeat a key" ] else []

        match schema @ entries @ duplicates @ stringListProblems "derivedFrom" block.DerivedFrom with
        | [] -> historyProblems block.Contributions
        | found -> found

    /// Tolerated forward-compatible content: operation codes this version does
    /// not know and that are not `x-...` extensions.
    let warnings (block: ProvenanceBlock) =
        block.Contributions
        |> List.collect (fun (key, entry) ->
            entry.Operations
            |> List.choose (function
                | Operation.Other code when not (extensionCode.IsMatch code) ->
                    Some $"contributions.%s{key}.operations: '%s{code}' is not an operation this version knows; preserved verbatim"
                | _ -> None))

    let private extensionStrings (extensions: Extensions) =
        extensions |> List.collect (fun (name, raw) -> [ name; raw.Text ])

    /// Every string a contribution carries, for the credential tripwire.
    let private contributionStrings key (entry: Contribution) =
        List.concat
            [ [ key; entry.At; entry.Actor.Id; ActorKind.code entry.Actor.Kind ]
              Option.toList entry.Last
              [ entry.Actor.Provider; entry.Actor.Model; entry.Actor.Runtime ] |> List.choose id
              extensionStrings entry.Actor.Extensions
              Option.toList entry.Reason
              Option.defaultValue [] entry.Evidence
              entry.Operations |> List.map Operation.code
              extensionStrings entry.Extensions ]

    let private hasCredential strings = strings |> List.exists isCredentialLike

    /// Appends one contribution without disturbing any other (RQ-ROS-2026-A004,
    /// VIG-PROV-005), following the reference library's `appendContribution`
    /// at contract revision 1.1:
    ///
    ///   * the same key merges operations and evidence, keeps the incoming
    ///     entry's unknown fields (the existing entry wins on conflict), and
    ///     sets `last` to the later of the two times -- only when the actor
    ///     agrees, and never for an actor of unknown identity extending an
    ///     entry a known actor holds;
    ///   * a second, late or merged-in `created` is refused;
    ///   * nothing is removed, reordered or re-attributed;
    ///   * whatever is returned is itself a valid block: an append that would
    ///     produce a malformed history (for example a contribution dated before
    ///     the creation) is refused.
    ///
    /// Returns the new block and whether anything changed: appending an
    /// identical contribution is a no-op.
    let append (key: string) (contribution: Contribution) (block: ProvenanceBlock) =
        let finish (next: ProvenanceBlock) changed =
            match problems next with
            | [] -> Ok(next, changed)
            | found -> Error $"""the resulting history would be malformed: %s{String.concat "; " found}"""

        let isKnown (value: string) =
            nonEmpty value && value.Trim() <> ProvenanceActor.UnknownValue

        match problems block with
        | _ :: _ as found -> Error $"""refusing to append to a malformed provenance block: %s{String.concat "; " found}"""
        | [] when hasCredential (contributionStrings key contribution) ->
            Error "a contribution must never carry authentication material"
        | [] ->
            match contributionProblems key contribution with
            | _ :: _ as found -> Error(String.concat "; " found)
            | [] ->
                let isCreation = contribution.Operations |> List.contains Operation.Created
                let hasOriginator = not (creators block.Contributions).IsEmpty

                match block.Contributions |> List.tryFind (fun (existingKey, _) -> existingKey = key) with
                | None ->
                    if isCreation && hasOriginator then
                        Error "the record already has an originator; record 'modified' instead of 'created'"
                    elif isCreation
                         && block.Contributions
                            |> List.exists (fun (_, entry) -> instant entry.At < instant contribution.At) then
                        Error "a 'created' contribution cannot follow existing contributions"
                    else
                        finish { block with Contributions = block.Contributions @ [ key, contribution ] } true
                | Some(_, existing) ->
                    if not (actorsAgree existing.Actor contribution.Actor) then
                        Error
                            $"contribution '%s{key}' is already attributed to %s{ActorKind.code existing.Actor.Kind}:%s{existing.Actor.Id}; refusing to re-attribute it"
                    elif (isKnown existing.Actor.Id && not (isKnown contribution.Actor.Id))
                         || (existing.Actor.Kind <> ActorKind.Unknown && contribution.Actor.Kind = ActorKind.Unknown) then
                        Error
                            $"contribution '%s{key}' belongs to %s{ActorKind.code existing.Actor.Kind}:%s{existing.Actor.Id}; an actor with unknown identity cannot extend it"
                    elif isCreation
                         && not (existing.Operations |> List.contains Operation.Created)
                         && (hasOriginator
                             || block.Contributions
                                |> List.exists (fun (other, entry) -> other <> key && instant entry.At < instant existing.At)) then
                        Error "the record's originator is already recorded or precedes this contribution; record 'modified' instead of 'created'"
                    else
                        let operations =
                            existing.Operations
                            @ (contribution.Operations
                               |> List.filter (fun op -> not (List.contains op existing.Operations)))

                        let existingEvidence = Option.defaultValue [] existing.Evidence

                        let evidence =
                            existingEvidence
                            @ (Option.defaultValue [] contribution.Evidence
                               |> List.filter (fun item -> not (List.contains item existingEvidence)))

                        let latest =
                            let mine = Option.defaultValue existing.At existing.Last
                            let theirs = Option.defaultValue contribution.At contribution.Last
                            if instant theirs > instant mine then theirs else mine

                        // Incoming unknown fields are kept; the existing entry
                        // wins on conflict.
                        let extensions =
                            existing.Extensions
                            @ (contribution.Extensions
                               |> List.filter (fun (name, _) ->
                                   not (existing.Extensions |> List.exists (fun (kept, _) -> kept = name))))

                        let merged =
                            { existing with
                                Operations = operations
                                Evidence =
                                    if not evidence.IsEmpty then Some evidence
                                    elif existing.Evidence.IsSome then existing.Evidence
                                    else contribution.Evidence
                                Last =
                                    if instant latest > instant existing.At then Some latest
                                    elif existing.Last.IsSome then existing.Last
                                    else contribution.Last
                                Reason =
                                    match existing.Reason with
                                    | None -> contribution.Reason
                                    | kept -> kept
                                Extensions = extensions }

                        let contributions =
                            block.Contributions
                            |> List.map (fun (k, entry) -> if k = key then k, merged else k, entry)

                        finish { block with Contributions = contributions } (merged <> existing)

    /// Adds lineage references (never authorship), preserving existing order
    /// (RQ-ROS-2026-A008, VIG-PROV-010).
    let addLineage (references: string list) (block: ProvenanceBlock) =
        let current = Option.defaultValue [] block.DerivedFrom

        let additions =
            references
            |> List.distinct
            |> List.filter (fun reference -> not (List.contains reference current))

        if additions.IsEmpty then
            block
        else
            { block with DerivedFrom = Some(current @ additions) }

    /// The originating (`created`) contribution, or `None` when origin is not
    /// recorded: a legacy record, or one whose history starts later.
    let originator (block: ProvenanceBlock) =
        match creators block.Contributions with
        | [ found ] -> Some found
        | _ -> None

    /// Contributions that played a role, oldest first.
    let withRole (operation: Operation) (block: ProvenanceBlock) =
        block.Contributions
        |> List.filter (fun (_, entry) -> entry.Operations |> List.contains operation)
        |> List.sortBy (fun (_, entry) -> instant entry.At)

    /// A contribution with no reason, evidence or extension fields.
    let contribution operations (at: Instant) actor =
        { Operations = operations
          At = timestampOf at
          Last = None
          Actor = actor
          Reason = None
          Evidence = None
          Extensions = [] }
