module Vigila.Application.Tests

open System.Text.Json
open Xunit
open Vigila.Application.Dispatch

// The engine's whole surface is JSON in, JSON out, so the tests drive it the
// way the kernel does rather than reaching past it.

let private event name value =
    match value with
    | Some v -> $"""{{"kind":"Event","event":{{"kind":"Event","name":"%s{name}","value":"%s{v}"}}}}"""
    | None -> $"""{{"kind":"Event","event":{{"kind":"Event","name":"%s{name}"}}}}"""

let private view json =
    let doc = JsonDocument.Parse(json: string)
    doc.RootElement.GetProperty "view"

let private str (v: JsonElement) (name: string) =
    match v.GetProperty(name).GetString() with
    | NonNull text -> text
    | Null -> failwith $"'%s{name}' was null"

let private flag (v: JsonElement) (name: string) = v.GetProperty(name).GetBoolean()
let private count (v: JsonElement) (name: string) = v.GetProperty(name).GetInt32()

/// Drives a sequence of messages from the initial state.
let private run messages =
    messages
    |> List.fold (fun (state, _) message -> step state message) (initial, "")

// ---------------------------------------------------------------------------
// The contract with the kernel
// ---------------------------------------------------------------------------

[<Fact>]
let ``a response always carries view, effects and cancellations`` () =
    // The kernel reads all three keys; omitting one when it is empty would
    // break it for no gain.
    let _, json = step initial (event "titleChanged" (Some "x"))
    let doc = JsonDocument.Parse json

    for key in [ "view"; "effects"; "cancellations" ] do
        Assert.True(doc.RootElement.TryGetProperty(key) |> fst, $"response is missing '%s{key}'")

[<Fact>]
let ``an unknown event is ignored rather than failing`` () =
    // The DOM is not authoritative: an unrecognised binding is a markup
    // mistake, not a domain error.
    let before, _ = step initial (event "titleChanged" (Some "Call the accountant"))
    let after, _ = step before (event "somethingNobodyDefined" (Some "x"))
    Assert.Equal<State>(before, after)

[<Fact>]
let ``initialize asks for the stored configuration`` () =
    // VIG-SEC-007 step 1: startup begins by reading what was stored, and asks
    // for it rather than assuming it, because the engine cannot touch
    // localStorage itself.
    let _, json = step initial """{"kind":"Initialize","protocolVersion":1,"capabilities":["Http","Storage"],"location":{"origin":"http://localhost","path":"/","query":"","hash":""}}"""

    use parsed = JsonDocument.Parse json
    let effects = parsed.RootElement.GetProperty "effects"

    let requested =
        effects.EnumerateArray()
        |> Seq.map (fun e ->
            match e.GetProperty("key").GetString() with
            | NonNull key -> key
            | Null -> "")
        |> Seq.toList

    Assert.Equal<string list>(
        [ "vigila.repository"; "vigila.branch"; "vigila.tokenPresent" ],
        requested
    )

    // The token's own key is never requested: the engine does not know it.
    Assert.DoesNotContain("vigila.token\"", json)

// ---------------------------------------------------------------------------
// Capture
// ---------------------------------------------------------------------------

[<Fact>]
let ``typing a title updates the draft and enables capture`` () =
    let _, json = step initial (event "titleChanged" (Some "Call accountant"))
    let v = view json

    Assert.Equal("Call accountant", str v "draft")
    Assert.False(flag v "captureDisabled")

[<Fact>]
let ``capture adds the item and clears the draft`` () =
    let _, json =
        run [ event "titleChanged" (Some "Call accountant about generator")
              event "capture" None ]

    let v = view json
    Assert.Equal(1, count v "itemCount")
    Assert.Equal("", str v "draft")
    Assert.False(flag v "isEmpty")
    Assert.True(flag v "hasItems")

[<Fact>]
let ``a captured item is projected with its title, kind and status`` () =
    let _, json =
        run [ event "titleChanged" (Some "Send the cancelled check")
              event "capture" None ]

    let item = (view json).GetProperty("items").EnumerateArray() |> Seq.head

    Assert.Equal("Send the cancelled check", str item "title")
    Assert.Equal("Task", str item "kind")
    Assert.Equal("Open", str item "status")
    Assert.StartsWith("VIG-", str item "id")

[<Fact>]
let ``the newest item is projected first`` () =
    let _, json =
        run [ event "titleChanged" (Some "first")
              event "capture" None
              event "titleChanged" (Some "second")
              event "capture" None ]

    let titles =
        (view json).GetProperty("items").EnumerateArray()
        |> Seq.map (fun i -> str i "title")
        |> Seq.toList

    Assert.Equal<string list>([ "second"; "first" ], titles)

// ---------------------------------------------------------------------------
// The rule lives in the domain, not here
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("")>]
[<InlineData(" ")>]
[<InlineData("\\t")>]
let ``capture is refused for a title the domain rejects`` (draft: string) =
    // VIG-DOM-048. The engine does not re-implement this rule; it asks
    // Title.create. If the two ever disagree, this test fails.
    let _, json = run [ event "titleChanged" (Some draft); event "capture" None ]
    let v = view json

    Assert.Equal(0, count v "itemCount")
    Assert.True(flag v "hasError")
    Assert.NotEmpty(str v "error")

[<Fact>]
let ``capture is disabled for exactly the drafts the domain would refuse`` () =
    // The projection and the domain must agree, or the button lies about what
    // will happen.
    for draft in [ ""; "   "; "ok"; "a title that is perfectly fine" ] do
        let state, json = step initial (event "titleChanged" (Some draft))
        let projected = flag (view json) "captureDisabled"
        let domainRefuses = Vigila.Semantic.Items.Title.create state.Draft |> Result.isError

        Assert.Equal(domainRefuses, projected)

[<Fact>]
let ``a title beyond the documented limit is refused`` () =
    // VIG-DOM-047, enforced by Title.MaxLength rather than by the engine.
    let tooLong = String.replicate (Vigila.Semantic.Items.Title.MaxLength + 1) "a"
    let _, json = run [ event "titleChanged" (Some tooLong); event "capture" None ]

    Assert.Equal(0, count (view json) "itemCount")
    Assert.True(flag (view json) "hasError")

[<Fact>]
let ``the surrounding whitespace the domain trims is trimmed here too`` () =
    let _, json = run [ event "titleChanged" (Some "  padded  "); event "capture" None ]
    let item = (view json).GetProperty("items").EnumerateArray() |> Seq.head
    Assert.Equal("padded", str item "title")

[<Fact>]
let ``editing after a refusal clears the message`` () =
    let refused, _ = run [ event "titleChanged" (Some ""); event "capture" None ]
    let _, json = step refused (event "titleChanged" (Some "now valid"))
    Assert.False(flag (view json) "hasError")

// ---------------------------------------------------------------------------
// The stateful entry point the WASM shim uses
// ---------------------------------------------------------------------------

[<Fact>]
let ``handle threads state across calls`` () =
    // The shim cannot hold state, so the engine does. This is the path the
    // browser actually takes.
    reset ()

    handle (event "titleChanged" (Some "through the shim")) |> ignore
    let json = handle (event "capture" None)

    Assert.Equal(1, count (view json) "itemCount")
    reset ()\n\n[<Fact>]\nlet ``handle captures malformed Limen JSON as a safe Aegis presentation`` () =\n    reset ()\n\n    let json = handle "{"\n    let v = view json\n\n    Assert.True(flag v "hasOperationalFault")\n    Assert.Equal("Vigila could not complete that operation", str v "operationalFaultTitle")\n    Assert.Contains("could not process an application message", str v "operationalFaultMessage")\n    Assert.StartsWith("AG-", str v "operationalFaultReference")\n    Assert.DoesNotContain("JsonException", json)\n\n    reset ()\n
