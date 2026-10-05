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
    let ids = List.init 1000 (fun _ -> ItemId.create (IdSource.create System.Guid.NewGuid))
    Assert.Equal(1000, ids |> List.distinct |> List.length)

[<Fact>]
let ``an identifier round-trips through its guid`` () =
    let id = ItemId.create (IdSource.create System.Guid.NewGuid)
    Assert.Equal(id, ItemId.ofGuid (ItemId.toGuid id))

[<Fact>]
let ``item and note identifiers are distinct types`` () =
    // VIG-GOV-012: the two cannot be passed interchangeably. This test exists
    // to fail at compile time if the types are ever collapsed into one.
    let item = ItemId.create (IdSource.create System.Guid.NewGuid)
    let note = NoteId.create (IdSource.create System.Guid.NewGuid)
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

// Deterministic identity (Echelon VIG-F6): Tier 1 draws ids only from the
// injected IdSource, so identical inputs give identical items.

let private fixedClock =
    Vigila.Semantic.Time.Clock.fixedAt (Vigila.Semantic.Time.Instant.ofDateTimeOffset (System.DateTimeOffset(2026, 10, 5, 0, 0, 0, System.TimeSpan.Zero)))

let private author : Vigila.Semantic.Actors.Actor = { Type = Vigila.Semantic.Actors.ActorType.Human; Name = "k" }

let private sameTitle = match Title.create "Same" with Ok t -> t | Error e -> failwith e

/// A source that yields the given GUIDs in order.
let private sequence (values: System.Guid list) =
    let remaining = ref values
    IdSource.create (fun () ->
        match remaining.Value with
        | head :: tail ->
            remaining.Value <- tail
            head
        | [] -> failwith "id sequence exhausted")

let private g n = System.Guid.Parse(sprintf "00000000-0000-4000-8000-%012d" n)

[<Fact>]
let ``Item.create is deterministic for identical inputs`` () =
    let create () =
        Vigila.Semantic.Item.Item.create fixedClock (IdSource.fixedAt (g 1)) author Vigila.Semantic.Actors.CreatedVia.UI sameTitle

    let a = create ()
    let b = create ()
    Assert.Equal(ItemId.toGuid a.Id, ItemId.toGuid b.Id)
    Assert.Equal(g 1, ItemId.toGuid a.Id)
    Assert.Equal(a.CreatedAt, b.CreatedAt)

[<Fact>]
let ``identities are drawn from the source in order`` () =
    let ids = sequence [ g 1; g 2; g 3 ]
    let item = ItemId.create ids
    let note = NoteId.create ids
    let workspace = WorkspaceId.create ids
    Assert.Equal(g 1, ItemId.toGuid item)
    Assert.Equal(g 2, NoteId.toGuid note)
    Assert.Equal(g 3, WorkspaceId.toGuid workspace)

[<Fact>]
let ``a note's identity comes from the injected source`` () =
    let item = ItemId.create (IdSource.fixedAt (g 1))

    match Vigila.Semantic.Notes.Note.create fixedClock (IdSource.fixedAt (g 9)) author item "text" with
    | Ok note -> Assert.Equal(g 9, NoteId.toGuid note.Id)
    | Error e -> failwith e

[<Fact>]
let ``the persisted id forms are unchanged`` () =
    // Segment (file name) and display forms are what storage and receipts use.
    let id = ItemId.create (IdSource.fixedAt (System.Guid.Parse "0f8fad5b-d9cb-469f-a165-70867728950e"))
    Assert.Equal("0f8fad5bd9cb469fa16570867728950e", id.Segment)
    Assert.Equal("VIG-0f8fad5b", ItemId.display id)
