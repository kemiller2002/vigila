/// The connection flow, driven the way the kernel drives it.
///
/// Every test here goes through `step` with real JSON, so what is asserted is
/// the wire behaviour and not an internal shape. The security tests in
/// particular would be worthless otherwise: the claim is about what crosses the
/// boundary, so the boundary is what is inspected.
///
/// Requirements: VIG-SEC-001, VIG-SEC-005, VIG-SEC-007, VIG-SEC-008,
/// VIG-SEC-009, VIG-SEC-012, VIG-SEC-014, VIG-SEC-015.
module Vigila.Application.ConnectionTests

open System.Text.Json
open Xunit
open Vigila.Application.Connection
open Vigila.Application.Dispatch

// ---------------------------------------------------------------------------
// Driving the engine
// ---------------------------------------------------------------------------

let private event name value =
    match value with
    | Some v -> $"""{{"kind":"Event","event":{{"kind":"Event","name":"%s{name}","value":"%s{v}"}}}}"""
    | None -> $"""{{"kind":"Event","event":{{"kind":"Event","name":"%s{name}"}}}}"""

let private initialize =
    """{"kind":"Initialize","protocolVersion":1,"capabilities":["Http","Storage"],"location":{"origin":"http://localhost","path":"/","query":"","hash":""}}"""

let private storageSuccess correlationId value =
    let rendered =
        match value with
        | Some v -> $"\"%s{v}\""
        | None -> "null"

    $"""{{"kind":"EffectResult","result":{{"kind":"StorageResult","correlationId":"%s{correlationId}","outcome":{{"kind":"Success","value":%s{rendered}}}}}}}"""

let private httpSuccess correlationId status body =
    $"""{{"kind":"EffectResult","result":{{"kind":"HttpResult","correlationId":"%s{correlationId}","outcome":{{"kind":"Success","status":%d{status},"body":%s{body}}}}}}}"""

let private httpFailure correlationId reason =
    $"""{{"kind":"EffectResult","result":{{"kind":"HttpResult","correlationId":"%s{correlationId}","outcome":{{"kind":"Failure","reason":"%s{reason}"}}}}}}"""

let private httpUnknown correlationId =
    $"""{{"kind":"EffectResult","result":{{"kind":"HttpResult","correlationId":"%s{correlationId}","outcome":{{"kind":"OutcomeUnknown","reason":"timeout-after-dispatch"}}}}}}"""

/// Threads a sequence of messages, keeping every response for inspection.
let private drive messages =
    messages
    |> List.fold
        (fun (state, responses) message ->
            let next, json = step state message
            next, responses @ [ json ])
        (initial, [])

let private lastView responses =
    let parsed = JsonDocument.Parse(List.last responses: string)
    parsed.RootElement.GetProperty "view"

let private text (view: JsonElement) name =
    match view.GetProperty(name: string).GetString() with
    | NonNull value -> value
    | Null -> ""

let private flag (view: JsonElement) (name: string) = view.GetProperty(name).GetBoolean()

let private effectsOf (json: string) =
    use parsed = JsonDocument.Parse json

    parsed.RootElement.GetProperty("effects").EnumerateArray()
    |> Seq.map (fun e -> e.Clone())
    |> Seq.toList

let private correlationIds json =
    effectsOf json
    |> List.map (fun e ->
        match e.GetProperty("correlationId").GetString() with
        | NonNull id -> id
        | Null -> "")

/// A repository response that grants push, which is how GitHub reports write
/// capability without a probe write (VIG-SEC-010).
let private repositoryBody canPush =
    $"""{{"full_name":"owner/data","permissions":{{"admin":false,"push":%b{canPush},"pull":true}}}}"""

/// Restores a stored configuration and returns the state together with the
/// correlation id of the read probe it issued.
///
/// The id is returned rather than assumed: a test that hard-codes one is
/// asserting against its own arithmetic instead of against the engine.
let private restoreStoredConfiguration () =
    let afterInit, initJson = step initial initialize
    let ids = correlationIds initJson

    let restored, lastJson =
        [ storageSuccess ids.[0] (Some "owner/data")
          storageSuccess ids.[1] (Some "main")
          storageSuccess ids.[2] (Some "yes") ]
        |> List.fold (fun (state, _) message -> step state message) (afterInit, "")

    restored, List.exactlyOne (correlationIds lastJson)

// ---------------------------------------------------------------------------
// VIG-SEC-005 - the token never crosses the boundary
// ---------------------------------------------------------------------------

[<Fact>]
let ``the engine never asks for the token's storage key`` () =
    let _, json = step initial initialize

    // It asks for three things, and the credential is not among them.
    let keys =
        effectsOf json
        |> List.map (fun e ->
            match e.GetProperty("key").GetString() with
            | NonNull key -> key
            | Null -> "")

    Assert.Equal<string list>([ Keys.Repository; Keys.Branch; Keys.TokenPresent ], keys)
    Assert.DoesNotContain(Keys.all, fun key -> key = "vigila.token")

[<Fact>]
let ``no request the engine emits carries an authorization header`` () =
    // The credential is attached kernel-side. If the engine ever started
    // building one, this is where it would show.
    let restored, _ = restoreStoredConfiguration ()
    let _, json = step restored (event "connect" None)

    Assert.DoesNotContain("Authorization", json)
    Assert.DoesNotContain("Bearer", json)
    Assert.DoesNotContain("headers", json)

[<Fact>]
let ``a token typed into the form reaches the engine only as a yes`` () =
    // connection.js sends "yes", never the credential. Even if something sent
    // the token itself, the engine stores no more than the fact.
    let state, json = step initial (event "tokenEntered" (Some "yes"))
    let view = lastView [ json ]

    Assert.True(flag view "hasToken" |> not) // presence alone does not connect
    Assert.False(flag view "connectDisabled")
    Assert.DoesNotContain("ghp_", json)
    ignore state

// ---------------------------------------------------------------------------
// VIG-SEC-007 - the startup sequence
// ---------------------------------------------------------------------------

[<Fact>]
let ``with nothing stored the user is sent to setup`` () =
    let afterInit, initJson = step initial initialize
    let ids = correlationIds initJson

    let _, responses =
        [ storageSuccess ids.[0] None
          storageSuccess ids.[1] None
          storageSuccess ids.[2] None ]
        |> List.fold
            (fun (state, acc) message ->
                let next, json = step state message
                next, acc @ [ json ])
            (afterInit, [])

    let view = lastView responses
    Assert.True(flag view "needsSetup")
    Assert.False(flag view "isConnected")
    Assert.Equal(BranchName.Default, text view "setupBranch")

[<Fact>]
let ``stored configuration is validated rather than trusted`` () =
    // VIG-SEC-008: the engine must not treat a stored token as valid merely
    // because it is there. Restoring must produce a request, not a connection.
    let restored, _ = restoreStoredConfiguration ()
    let _, json = step restored (event "nothing" None)
    let view = lastView [ json ]

    Assert.False(flag view "isConnected")
    Assert.True(flag view "isValidating")

[<Fact>]
let ``a full successful connection ends connected`` () =
    let restored, readId = restoreStoredConfiguration ()

    let afterRead, readJson = step restored (httpSuccess readId 200 (repositoryBody true))

    // The branch probe is issued only after the read probe succeeds, which is
    // what VIG-SEC-009's "a read does not imply a write" looks like in the
    // sequence rather than in prose.
    let branchId = List.head (correlationIds readJson)
    let _, branchJson = step afterRead (httpSuccess branchId 200 "{\"name\":\"main\"}")

    let view = lastView [ branchJson ]
    Assert.True(flag view "isConnected")
    Assert.False(flag view "needsSetup")
    Assert.Equal("Connected", text view "connectionStatus")

// ---------------------------------------------------------------------------
// VIG-SEC-012 / VIG-SEC-015 - failures are specific
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData(401, "Unauthorized")>]
[<InlineData(403, "Forbidden")>]
[<InlineData(404, "RepositoryNotFound")>]
[<InlineData(500, "RepositoryUnavailable")>]
let ``each repository failure is reported as its own code`` (status: int) (expected: string) =
    let restored, readId = restoreStoredConfiguration ()
    let _, json = step restored (httpSuccess readId status "{}")
    let view = lastView [ json ]

    Assert.Equal(expected, text view "connectionCode")
    Assert.True(flag view "needsSetup")
    Assert.False(flag view "isConnected")

[<Fact>]
let ``a readable repository with no push access is not a generic failure`` () =
    // VIG-SEC-009: a successful read must not be taken to imply a write.
    let restored, readId = restoreStoredConfiguration ()
    let _, json = step restored (httpSuccess readId 200 (repositoryBody false))
    let view = lastView [ json ]

    Assert.Equal("WriteAccessMissing", text view "connectionCode")
    Assert.False(flag view "isConnected")

[<Fact>]
let ``a missing branch is distinguished from a missing repository`` () =
    // VIG-SEC-015: branch protection and branch absence must not collapse into
    // a generic save failure.
    let restored, readId = restoreStoredConfiguration ()
    let afterRead, readJson = step restored (httpSuccess readId 200 (repositoryBody true))
    let branchId = List.head (correlationIds readJson)
    let _, json = step afterRead (httpSuccess branchId 404 "{}")
    let view = lastView [ json ]

    Assert.Equal("BranchUnavailable", text view "connectionCode")

[<Fact>]
let ``an unreachable GitHub is reported as unreachable, not as a bad token`` () =
    let restored, readId = restoreStoredConfiguration ()
    let _, json = step restored (httpFailure readId "network")
    let view = lastView [ json ]

    Assert.Equal("RepositoryUnavailable", text view "connectionCode")
    Assert.True(isRetryable RepositoryUnavailable)

[<Fact>]
let ``an unobserved outcome is never reported as a success`` () =
    // VIG-UI-014: a request that may or may not have happened is not a
    // connection.
    let restored, readId = restoreStoredConfiguration ()
    let _, json = step restored (httpUnknown readId)
    let view = lastView [ json ]

    Assert.False(flag view "isConnected")
    Assert.Equal("RepositoryUnavailable", text view "connectionCode")

// ---------------------------------------------------------------------------
// VIG-SEC-001 - configuration rules
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("owner")>]
[<InlineData("owner/")>]
[<InlineData("/data")>]
[<InlineData("owner/data/extra")>]
let ``a repository that is not owner slash name is refused`` (raw: string) =
    Assert.True(Result.isError (RepositoryId.create raw))

[<Fact>]
let ``a repository is accepted and keeps its parts`` () =
    match RepositoryId.create "  echelon-foundry/vigila-data  " with
    | Ok repository ->
        Assert.Equal("echelon-foundry", RepositoryId.owner repository)
        Assert.Equal("vigila-data", RepositoryId.name repository)
        Assert.Equal("echelon-foundry/vigila-data", RepositoryId.value repository)
    | Error refusal -> failwith $"expected a repository, got %A{refusal}"

[<Theory>]
[<InlineData("has space")>]
[<InlineData("caret^")>]
[<InlineData("tilde~")>]
[<InlineData("colon:")>]
[<InlineData("dots..here")>]
[<InlineData("/leading")>]
[<InlineData("trailing/")>]
[<InlineData("-leading-dash")>]
[<InlineData("")>]
let ``a branch git would refuse is refused here`` (raw: string) =
    Assert.True(Result.isError (BranchName.create raw))

[<Fact>]
let ``connect without a token refuses before any request is made`` () =
    let _, json =
        drive
            [ event "repositoryChanged" (Some "owner/data")
              event "connect" None ]
        |> snd
        |> fun responses -> (), List.last responses

    let view = lastView [ json ]
    Assert.True(flag view "hasSetupError")
    Assert.Empty(effectsOf json)

[<Fact>]
let ``connect with a malformed repository says what is wrong`` () =
    let _, responses =
        drive
            [ event "tokenEntered" (Some "yes")
              event "repositoryChanged" (Some "not-a-repository")
              event "connect" None ]

    let json = List.last responses
    let view = lastView responses

    Assert.True(flag view "hasSetupError")
    Assert.Contains("owner/repository", text view "setupError")
    Assert.Empty(effectsOf json)

// ---------------------------------------------------------------------------
// VIG-SEC-006 - disconnecting
// ---------------------------------------------------------------------------

[<Fact>]
let ``disconnect forgets the credential and keeps the repository`` () =
    let restored, readId = restoreStoredConfiguration ()
    let afterRead, readJson = step restored (httpSuccess readId 200 (repositoryBody true))
    let branchId = List.head (correlationIds readJson)
    let connected, _ = step afterRead (httpSuccess branchId 200 "{\"name\":\"main\"}")

    let _, json = step connected (event "disconnect" None)
    let view = lastView [ json ]

    Assert.False(flag view "isConnected")
    Assert.True(flag view "needsSetup")
    // VIG-SEC-006: clearing the token does not delete repository data, and the
    // repository itself stays configured so reconnecting is not re-typing.
    Assert.Equal("owner/data", text view "setupRepository")

    // The only thing removed is the presence flag; the engine cannot remove the
    // token because it does not know where it is.
    let removed =
        effectsOf json
        |> List.map (fun e ->
            match e.GetProperty("key").GetString() with
            | NonNull key -> key
            | Null -> "")

    Assert.Equal<string list>([ Keys.TokenPresent ], removed)

[<Fact>]
let ``acknowledging a saved setting does not erase it`` () =
    // A storage write is acknowledged with the same message shape as a storage
    // read. Tagging the write with a read's meaning made the acknowledgement
    // look like "restored an empty value", which cleared the repository the
    // connection had just saved. Found by the browser suite; pinned here.
    let restored, readId = restoreStoredConfiguration ()
    let afterRead, readJson = step restored (httpSuccess readId 200 (repositoryBody true))
    let branchId = List.head (correlationIds readJson)
    let connected, saveJson = step afterRead (httpSuccess branchId 200 "{\"name\":\"main\"}")

    // Answer both persistence writes the way localStorage does: success, no
    // value.
    let final, responses =
        correlationIds saveJson
        |> List.fold
            (fun (state, acc) id ->
                let next, json = step state (storageSuccess id None)
                next, acc @ [ json ])
            (connected, [])

    ignore final
    let view = lastView responses

    Assert.Equal("owner/data", text view "setupRepository")
    Assert.Equal("main", text view "setupBranch")
    Assert.True(flag view "isConnected")
