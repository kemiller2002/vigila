/// Tier 3 - the `praxis.provenance/1` JSON codec.
///
/// Receives a block at a boundary and decides what may be done with it
/// (VIG-PROV-006), mirroring `classify` in the Praxis reference library
/// `lib/provenance-interchange.mjs` and pinned to the same fixtures
/// (`tests/fixtures/praxis-provenance/cases.json`, VIG-PROV-017):
///
///   * `Supported` -- a block this version understands. Every field it does
///     not model is kept as opaque JSON in the typed block's `Extensions`, so
///     writing it back loses nothing; operation codes it does not know are
///     kept and reported as warnings.
///   * `Unsupported` -- another major version. Kept as the exact JSON text,
///     never interpreted, merged into or appended to.
///   * `Malformed` -- rejected with every problem named. Never dropped,
///     never repaired.
///
/// It lives in Tier 3 because Tier 1 and Tier 2 may not reference
/// System.Text.Json (scripts/check-architecture.sh); the rules themselves are
/// Tier 1's (Vigila.Semantic.Provenance).
///
/// Requirements: VIG-PROV-006, VIG-PROV-013, VIG-PROV-017.
module Vigila.Application.ProvenanceJson

open System
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open Vigila.Semantic.Provenance

/// What a receiver may do with a block (VIG-PROV-006).
type Verdict =
    | Supported of block: ProvenanceBlock * warnings: string list
    | Unsupported of schema: string * json: RawJson
    | Malformed of problems: string list

let private majorTag = Regex("^praxis\\.provenance/([1-9][0-9]*)\\z", RegexOptions.CultureInvariant)

/// A property as JSON distinguishes it: absent, explicitly null, or a value.
/// The distinction matters: the contract treats `"last": null` as malformed,
/// not as an absent `last`.
[<NoComparison; NoEquality>]
type private Field =
    | Absent
    | JsonNull
    | Value of JsonNode

let private field (o: JsonObject) (name: string) =
    let mutable found: JsonNode | null = null

    if o.TryGetPropertyValue(name, &found) then
        match found with
        | Null -> JsonNull
        | NonNull node -> Value node
    else
        Absent

let private stringOf (node: JsonNode) =
    match node with
    | :? JsonValue as value when value.GetValueKind() = JsonValueKind.String -> Some(value.GetValue<string>())
    | _ -> None

let private raw (node: JsonNode | null) =
    match node with
    | Null -> RawJson "null"
    | NonNull value -> RawJson(value.ToJsonString())

let private extras (o: JsonObject) (known: string list) : Extensions =
    o
    |> Seq.filter (fun pair -> not (List.contains pair.Key known))
    |> Seq.map (fun pair -> pair.Key, raw pair.Value)
    |> Seq.toList

/// Dotted paths of every member name or string value that matches `test`.
let private stringFindings (test: string -> bool) (node: JsonNode | null) =
    let rec walk (path: string) (current: JsonNode | null) : string list =
        match current with
        | :? JsonObject as o ->
            o
            |> Seq.collect (fun pair ->
                let child = if path.Length = 0 then pair.Key else $"%s{path}.%s{pair.Key}"
                (if test pair.Key then [ child ] else []) @ walk child pair.Value)
            |> Seq.toList
        | :? JsonArray as items ->
            items |> Seq.mapi (fun index item -> walk $"%s{path}[%d{index}]" item) |> Seq.concat |> Seq.toList
        | :? JsonValue as value ->
            match stringOf value with
            | Some text when test text -> [ path ]
            | _ -> []
        | _ -> []

    walk "" node

/// Dotted paths of every key or string value that looks like a credential
/// (VIG-PROV-013).
let credentialFindings (node: JsonNode | null) = stringFindings Provenance.isCredentialLike node

/// Dotted paths of every key or string value holding an unpaired UTF-16
/// surrogate (contract 1.2). A surrogate escaped in parsed JSON text throws
/// when read, which `classify` also reports as malformed.
let surrogateFindings (node: JsonNode | null) = stringFindings ContractText.hasLoneSurrogate node

let private notWellFormed = "provenance must be well-formed Unicode (unpaired UTF-16 surrogate)"

/// Why JSON text is not well-formed provenance text (contract 1.2 rule 1),
/// checked on the text itself, whatever its major version:
///
///   * it is not valid JSON (no comments, no trailing commas);
///   * any one object repeats a member name -- readers disagree about which
///     duplicate wins, so a second `created` could be smuggled past one of
///     them. Detected with `Utf8JsonReader` and a set per object, because
///     `JsonNode` accepts duplicates and throws `ArgumentException` only
///     when the object is walked;
///   * any member name or string holds an unpaired UTF-16 surrogate, raw or
///     escaped.
///
/// Never throws. Empty when the text is well-formed.
let textProblems (text: string) : string list =
    if isNull (box text) then
        [ "provenance text must be a string" ]
    elif ContractText.hasLoneSurrogate text then
        [ notWellFormed ]
    else
        try
            let bytes = Text.Encoding.UTF8.GetBytes text

            let mutable reader =
                Utf8JsonReader(
                    ReadOnlySpan<byte>(bytes),
                    JsonReaderOptions(CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false)
                )

            // Innermost container first: its member-name set (None for an
            // array) and its path.
            let mutable frames: (Collections.Generic.HashSet<string> option * string) list = []
            let mutable pending = ""
            let mutable repeated: string list = []

            while reader.Read() do
                match reader.TokenType with
                | JsonTokenType.StartObject -> frames <- (Some(Collections.Generic.HashSet<string>(StringComparer.Ordinal)), pending) :: frames
                | JsonTokenType.StartArray -> frames <- (None, pending) :: frames
                | JsonTokenType.EndObject
                | JsonTokenType.EndArray ->
                    frames <- List.tail frames
                    pending <- (match frames with (_, path) :: _ -> path | [] -> "")
                | JsonTokenType.PropertyName ->
                    let name = reader.GetString() |> Option.ofObj |> Option.defaultValue ""

                    match frames with
                    | (Some seen, path) :: _ ->
                        let child = if path.Length = 0 then name else $"%s{path}.%s{name}"
                        if not (seen.Add name) then repeated <- child :: repeated
                        pending <- child
                    | _ -> ()
                | JsonTokenType.String -> reader.GetString() |> ignore
                | _ -> ()

            repeated |> List.rev |> List.map (fun path -> $"%s{path}: member name repeated within one object")
        with
        | :? JsonException as ex -> [ $"provenance is not valid JSON: %s{ex.Message}" ]
        | :? InvalidOperationException -> [ notWellFormed ]
        | :? ArgumentException as ex -> [ $"provenance is not valid JSON: %s{ex.Message}" ]

let private optionalString prefix name (o: JsonObject) message =
    match field o name with
    | Absent -> Ok None
    | Value node when (stringOf node).IsSome -> Ok(stringOf node)
    | _ -> Error [ $"%s{prefix}.%s{name} %s{message}" ]

let private stringList label (node: Field) =
    match node with
    | Absent -> Ok None
    | Value(:? JsonArray as items) ->
        let values = items |> Seq.map (fun item -> item |> Option.ofObj |> Option.bind stringOf) |> Seq.toList

        if values |> List.forall Option.isSome then
            Ok(Some(values |> List.choose id))
        else
            Error [ $"%s{label} must be an array of non-empty strings" ]
    | _ -> Error [ $"%s{label} must be an array of non-empty strings" ]

let private combine results =
    let problems = results |> List.collect (function Error p -> p | Ok _ -> [])
    problems

/// Reads a Praxis actor. Structural problems only; the actor rules (agent
/// needs provider/model/runtime, id not empty) are Tier 1's.
let parseActor (prefix: string) (node: JsonNode | null) : Result<ProvenanceActor, string list> =
    match node with
    | :? JsonObject as o ->
        let kind =
            match field o "kind" with
            | Value n ->
                match stringOf n |> Option.bind ActorKind.tryParse with
                | Some kind -> Ok kind
                | None -> Error [ $"%s{prefix}.kind '%s{n.ToJsonString()}' is not agent, human, automation, unknown, or x-..." ]
            | _ -> Error [ $"%s{prefix}.kind is required (agent, human, automation, unknown, or x-...)" ]

        let id =
            match field o "id" with
            | Value n when (stringOf n).IsSome -> Ok(Option.get (stringOf n))
            | _ -> Error [ $"%s{prefix}.id must not be empty; use 'unknown' when it is not known" ]

        let attribute name = optionalString prefix name o "must be a non-empty string"
        let provider = attribute "provider"
        let model = attribute "model"
        let runtime = attribute "runtime"

        match kind, id, provider, model, runtime with
        | Ok kind, Ok id, Ok provider, Ok model, Ok runtime ->
            let actor =
                { Kind = kind
                  Id = id
                  Provider = provider
                  Model = model
                  Runtime = runtime
                  Extensions = extras o [ "kind"; "id"; "provider"; "model"; "runtime" ] }

            match Provenance.actorProblems prefix actor with
            | [] -> Ok actor
            | problems -> Error problems
        | _ ->
            Error(
                combine [ Result.map ignore kind
                          Result.map ignore id
                          Result.map ignore provider
                          Result.map ignore model
                          Result.map ignore runtime ]
            )
    | _ -> Error [ $"%s{prefix} must be an object" ]

let private parseContribution (key: string) (node: JsonNode | null) : Result<Contribution, string list> =
    let prefix = $"contributions.%s{key}"

    match node with
    | :? JsonObject as o ->
        let operations =
            match field o "operations" with
            | Value(:? JsonArray as items) ->
                let codes = items |> Seq.map (fun item -> item |> Option.ofObj |> Option.bind stringOf) |> Seq.toList

                if codes |> List.forall Option.isSome then
                    Ok(codes |> List.choose id |> List.map Operation.ofCode)
                else
                    Error [ $"%s{prefix}.operations: every operation must be a string code" ]
            | _ -> Error [ $"%s{prefix}.operations must be an array" ]

        let at =
            match field o "at" with
            | Value n when (stringOf n).IsSome -> Ok(Option.get (stringOf n))
            | _ -> Error [ $"%s{prefix}.at must be an ISO-8601 UTC timestamp" ]

        let last = optionalString prefix "last" o "must be an ISO-8601 UTC timestamp"
        let reason = optionalString prefix "reason" o "must be a string"
        let evidence = stringList $"%s{prefix}.evidence" (field o "evidence")

        let actor =
            match field o "actor" with
            | Absent -> Error [ $"%s{prefix}.actor is required" ]
            | JsonNull -> Error [ $"%s{prefix}.actor must be an object" ]
            | Value n -> parseActor $"%s{prefix}.actor" n

        match operations, at, last, reason, evidence, actor with
        | Ok operations, Ok at, Ok last, Ok reason, Ok evidence, Ok actor ->
            Ok
                { Operations = operations
                  At = at
                  Last = last
                  Actor = actor
                  Reason = reason
                  Evidence = evidence
                  Extensions = extras o [ "operations"; "at"; "last"; "actor"; "reason"; "evidence" ] }
        | _ ->
            Error(
                combine [ Result.map ignore operations
                          Result.map ignore at
                          Result.map ignore last
                          Result.map ignore reason
                          Result.map ignore evidence
                          Result.map ignore actor ]
            )
    | _ -> Error [ $"%s{prefix} must be an object" ]

let private classifyObject (block: JsonObject) =
    let schema =
        match field block "schema" with
        | Absent -> Ok None
        | Value n when (stringOf n).IsSome -> Ok(stringOf n)
        | _ -> Error "schema must be a string"

    match schema with
    | Error problem -> Malformed [ problem ]
    | Ok(Some tag) when tag <> Provenance.SchemaTag ->
        if majorTag.IsMatch tag then
            Unsupported(tag, RawJson(block.ToJsonString()))
        else
            Malformed [ $"schema '%s{tag}' is not a valid praxis.provenance/<major> tag" ]
    | Ok schema ->
        match field block "contributions" with
        | Absent -> Malformed [ "contributions is required" ]
        | Value(:? JsonObject as contributions) ->
            let parsed =
                contributions
                |> Seq.map (fun pair -> pair.Key, parseContribution pair.Key pair.Value)
                |> Seq.toList

            let lineage = stringList "derivedFrom" (field block "derivedFrom")

            let structural =
                (parsed |> List.collect (fun (_, r) -> match r with Error p -> p | Ok _ -> []))
                @ (match lineage with Error p -> p | Ok _ -> [])

            match structural, lineage with
            | [], Ok derivedFrom ->
                let typed =
                    { Schema = schema
                      Contributions = parsed |> List.choose (fun (k, r) -> match r with Ok c -> Some(k, c) | Error _ -> None)
                      DerivedFrom = derivedFrom
                      Extensions = extras block [ "schema"; "contributions"; "derivedFrom" ] }

                match Provenance.problems typed with
                | [] -> Supported(typed, Provenance.warnings typed)
                | problems -> Malformed problems
            | problems, _ -> Malformed problems
        | _ -> Malformed [ "contributions must be an object keyed by EXE-, EXT-, or CTB- keys" ]

/// Classifies a received block (VIG-PROV-006). An unpaired surrogate or a
/// credential-like value anywhere, even inside another major version, makes it
/// malformed (contract 1.2, VIG-PROV-013). Never throws: a parsed object that
/// repeats a member name (`ArgumentException` when walked), an escaped lone
/// surrogate (`InvalidOperationException` when read) or any other JSON fault
/// is malformed. Text should go through `classifyText`, which also sees
/// duplicates the parsed form has already lost.
let classify (node: JsonNode | null) : Verdict =
    try
        match node with
        | :? JsonObject as block ->
            match surrogateFindings block, credentialFindings block with
            | (_ :: _ as paths), _ ->
                Malformed(paths |> List.map (fun path -> $"%s{path}: unpaired UTF-16 surrogate; %s{notWellFormed}"))
            | [], [] -> classifyObject block
            | [], paths ->
                Malformed(
                    paths
                    |> List.map (fun path -> $"%s{path}: credential-like value; provenance must never carry authentication material")
                )
        | _ -> Malformed [ "provenance must be a JSON object" ]
    with
    | :? ArgumentException as ex -> Malformed [ $"provenance is not a valid JSON object: %s{ex.Message}" ]
    | :? InvalidOperationException -> Malformed [ notWellFormed ]
    | :? JsonException as ex -> Malformed [ $"provenance is not valid JSON: %s{ex.Message}" ]

/// Classifies a block received as JSON text (contract 1.2 rule 1): the text
/// is checked first (`textProblems`), then parsed and classified. Never
/// throws.
let classifyText (json: string) =
    match textProblems json with
    | _ :: _ as problems -> Malformed problems
    | [] ->
        try
            classify (JsonNode.Parse json)
        with
        | :? JsonException as ex -> Malformed [ $"provenance is not valid JSON: %s{ex.Message}" ]
        | :? ArgumentException as ex -> Malformed [ $"provenance is not valid JSON: %s{ex.Message}" ]
        | :? InvalidOperationException -> Malformed [ notWellFormed ]

/// The item form of a verdict: `None` when the block must be rejected.
let toItemProvenance verdict =
    match verdict with
    | Supported(block, _) -> Ok(Recorded block)
    | Unsupported(schema, json) -> Ok(CarriedVerbatim(schema, json))
    | Malformed problems -> Error problems

// --- writing --------------------------------------------------------------

let private rawNode (value: RawJson) : JsonNode | null = JsonNode.Parse value.Text

let private addExtensions (o: JsonObject) (extensions: Extensions) =
    for name, value in extensions do
        o[name] <- rawNode value

let private strings (values: string list) =
    let items = JsonArray()

    for value in values do
        items.Add(JsonValue.Create value)

    items

let actorToNode (actor: ProvenanceActor) =
    let o = JsonObject()
    o["kind"] <- JsonValue.Create(ActorKind.code actor.Kind)
    o["id"] <- JsonValue.Create actor.Id
    actor.Provider |> Option.iter (fun v -> o["provider"] <- JsonValue.Create v)
    actor.Model |> Option.iter (fun v -> o["model"] <- JsonValue.Create v)
    actor.Runtime |> Option.iter (fun v -> o["runtime"] <- JsonValue.Create v)
    addExtensions o actor.Extensions
    o

let private contributionToNode (entry: Contribution) =
    let o = JsonObject()
    o["operations"] <- strings (entry.Operations |> List.map Operation.code)
    o["at"] <- JsonValue.Create entry.At
    entry.Last |> Option.iter (fun v -> o["last"] <- JsonValue.Create v)
    o["actor"] <- actorToNode entry.Actor
    entry.Reason |> Option.iter (fun v -> o["reason"] <- JsonValue.Create v)
    entry.Evidence |> Option.iter (fun v -> o["evidence"] <- strings v)
    addExtensions o entry.Extensions
    o

/// The canonical JSON form of a supported block. Known fields are written in
/// the contract's order; fields this version does not model follow, unchanged,
/// in the order they were received. Contributions keep their order.
let toNode (block: ProvenanceBlock) =
    let o = JsonObject()
    block.Schema |> Option.iter (fun v -> o["schema"] <- JsonValue.Create v)
    let contributions = JsonObject()

    for key, entry in block.Contributions do
        contributions[key] <- contributionToNode entry

    o["contributions"] <- contributions
    block.DerivedFrom |> Option.iter (fun v -> o["derivedFrom"] <- strings v)
    addExtensions o block.Extensions
    o

let toJsonText (block: ProvenanceBlock) = (toNode block).ToJsonString()
