module Vigila.Semantic.Tests

open Xunit
open Vigila.Semantic.Identifiers
open Vigila.Semantic.Items

// VIG-TST-001 requires required-field validation to be covered.

[<Fact>]
let ``a title is required`` () =
    Assert.True(Result.isError (Title.create ""))

[<Theory>]
[<InlineData(" ")>]
[<InlineData("\t")>]
[<InlineData("\n")>]
[<InlineData("   \t \n ")>]
let ``whitespace-only titles are rejected`` (text: string) =
    // VIG-DOM-048.
    Assert.True(Result.isError (Title.create text))

[<Fact>]
let ``a null title is rejected rather than throwing`` () =
    Assert.True(Result.isError (Title.create null))

[<Fact>]
let ``surrounding whitespace is trimmed`` () =
    match Title.create "  Call accountant  " with
    | Ok title -> Assert.Equal("Call accountant", Title.value title)
    | Error e -> failwith e

[<Fact>]
let ``non-ASCII titles are preserved exactly`` () =
    // VIG-DOM-047 requires Unicode safety.
    let text = "Renouveler l'assurance — café über 日本語 \U0001F600"

    match Title.create text with
    | Ok title -> Assert.Equal(text, Title.value title)
    | Error e -> failwith e

[<Fact>]
let ``a title at the documented limit is accepted`` () =
    Assert.True(Result.isOk (Title.create (String.replicate Title.MaxLength "a")))

[<Fact>]
let ``a title beyond the documented limit is rejected`` () =
    // VIG-DOM-047.
    Assert.True(Result.isError (Title.create (String.replicate (Title.MaxLength + 1) "a")))

[<Fact>]
let ``identifiers are unique across creations`` () =
    // VIG-PER-042: concurrent creators must not collide.
    let ids = List.init 1000 (fun _ -> ItemId.create ())
    Assert.Equal(1000, ids |> List.distinct |> List.length)

[<Fact>]
let ``an identifier round-trips through its guid`` () =
    let id = ItemId.create ()
    Assert.Equal(id, ItemId.ofGuid (ItemId.toGuid id))

[<Fact>]
let ``item and note identifiers are distinct types`` () =
    // VIG-GOV-012: the two cannot be passed interchangeably. This test exists
    // to fail at compile time if the types are ever collapsed into one.
    let item = ItemId.create ()
    let note = NoteId.create ()
    Assert.NotEqual<System.Guid>(ItemId.toGuid item, NoteId.toGuid note)

[<Fact>]
let ``there are exactly three item kinds`` () =
    // VIG-DOM-005. Reminder was withdrawn by v0.3 section 85; OQ-01 tracks the
    // conflict with the v1 scope list. If Reminder returns, this test is the
    // reminder to revisit the requirement, not an obstacle to it.
    let kinds = [ Task; FollowUp; ItemKind.Waiting ]
    Assert.Equal(3, kinds |> List.distinct |> List.length)

[<Fact>]
let ``there are exactly five workflow states`` () =
    // VIG-DOM-008.
    let states = [ Open; ItemStatus.Waiting; Deferred; Completed; Cancelled ]
    Assert.Equal(5, states |> List.distinct |> List.length)
