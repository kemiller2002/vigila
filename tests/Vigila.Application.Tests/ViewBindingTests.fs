/// The HTML and the engine must agree, and the agreement is checked here.
///
/// Limen fills a binding from the engine's `ViewState` by name, so a key the
/// engine stops projecting, or a binding misspelled in the HTML, fails in
/// silence: the element simply never updates.
///
/// This replaces a shell script that grepped `Dispatch.fs` for the key. That
/// check passed for any key whose text appeared anywhere in the file, comments
/// included — `data-text="browser"` satisfied it and rendered nothing. The
/// agreement is with the *emitted JSON*, so that is what is compared here.
///
/// Requirements: VIG-GOV-010, VIG-GOV-013.
module Vigila.Application.ViewBindingTests

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Xunit
open Vigila.Application.Dispatch

/// Walks up from the test assembly until the repository root is found.
///
/// The marker is the file under test, so a wrong answer fails loudly rather
/// than silently checking nothing.
let private repositoryRoot =
    let rec search (dir: DirectoryInfo | null) =
        match dir with
        | Null -> failwith "Could not locate the repository root above the test assembly."
        | NonNull d ->
            if File.Exists(Path.Combine(d.FullName, "web", "index.html")) then
                d.FullName
            else
                search d.Parent

    search (DirectoryInfo AppContext.BaseDirectory)

let private html =
    File.ReadAllText(Path.Combine(repositoryRoot, "web", "index.html"))

let private matches pattern =
    Regex.Matches(html, pattern)
    |> Seq.map (fun m -> m.Groups.[1].Value)
    |> Set.ofSeq

/// Every key a binding names: data-text, data-if, data-each and data-bind-*.
let private boundKeys =
    matches "data-(?:text|if|each)=\"([A-Za-z]+)\""
    |> Set.union (matches "data-bind-[a-z]+=\"([A-Za-z]+)\"")

let private eventNames = matches "data-event=\"([A-Za-z]+)\""

/// The names the engine actually emits: the view's own keys, plus the fields of
/// the rows inside any array it projects.
let private emittedNames () =
    // One item is captured first so the arrays are not empty; an empty array
    // would let a missing row field pass unnoticed.
    let _, json =
        [ """{"kind":"Event","event":{"kind":"Event","name":"titleChanged","value":"A captured item"}}"""
          """{"kind":"Event","event":{"kind":"Event","name":"capture"}}""" ]
        |> List.fold (fun (state, _) message -> step state message) (initial, "")

    use parsed = JsonDocument.Parse json
    let view = parsed.RootElement.GetProperty "view"

    let rowFields (value: JsonElement) =
        if value.ValueKind = JsonValueKind.Array then
            value.EnumerateArray()
            |> Seq.collect (fun row -> row.EnumerateObject() |> Seq.map (fun f -> f.Name))
            |> Set.ofSeq
        else
            Set.empty

    view.EnumerateObject()
    |> Seq.fold
        (fun names property -> names |> Set.add property.Name |> Set.union (rowFields property.Value))
        Set.empty

[<Fact>]
let ``every data-* binding in the page names something the engine emits`` () =
    let emitted = emittedNames ()
    let unknown = Set.difference boundKeys emitted

    Assert.True(
        Set.isEmpty unknown,
        $"""index.html binds keys the engine does not emit: %s{String.Join(", ", unknown)}."""
    )

[<Fact>]
let ``every data-event in the page maps to a command the engine understands`` () =
    let unhandled =
        eventNames
        |> Set.filter (fun name -> eventToCommand name None = Ignored)

    Assert.True(
        Set.isEmpty unhandled,
        $"""index.html fires events with no case in eventToCommand: %s{String.Join(", ", unhandled)}."""
    )

[<Fact>]
let ``the check is looking at a real page with real bindings`` () =
    // Guards the guard: a regex that silently matched nothing would make both
    // tests above pass forever.
    Assert.NotEmpty boundKeys
    Assert.NotEmpty eventNames
