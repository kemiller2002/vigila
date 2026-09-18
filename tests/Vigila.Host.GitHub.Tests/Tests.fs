module Vigila.Host.GitHub.Tests

open Xunit
open Vigila.Semantic.Identifiers
open Vigila.Host.GitHub.StorageLayout

let private ok result =
    match result with
    | Ok v -> v
    | Error(e: PathRefusal) -> failwith e.Describe

let private root = ok (StoragePath.create "vigila")

// ---------------------------------------------------------------------------
// Path traversal and normalisation. VIG-TST-016 requires these explicitly.
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("..")>]
[<InlineData("../vigila")>]
[<InlineData("vigila/..")>]
[<InlineData("vigila/../../etc")>]
[<InlineData("a/../../../b")>]
[<InlineData("vigila/../chrona")>]
[<InlineData("../../")>]
let ``a path containing a traversal segment is refused`` (raw: string) =
    // VIG-PER-006. Each of these would otherwise resolve outside the
    // configured area, which VIG-PER-005 forbids.
    match StoragePath.create raw with
    | Error(Traversal _) -> ()
    | other -> failwith $"'%s{raw}' should be refused as traversal, got %A{other}"

[<Theory>]
[<InlineData(".")>]
[<InlineData("./vigila")>]
[<InlineData("vigila/./items")>]
let ``a path containing a relative segment is refused`` (raw: string) =
    match StoragePath.create raw with
    | Error RelativeSegment -> ()
    | other -> failwith $"'%s{raw}' should be refused as relative, got %A{other}"

[<Theory>]
[<InlineData("vigila\\items")>]
[<InlineData("..\\etc")>]
[<InlineData("\\")>]
let ``a backslash separator is refused`` (raw: string) =
    // VIG-PER-043: naming must be safe across repository environments. A
    // backslash is a separator on one platform and a legal filename character
    // on another, so it is never accepted.
    match StoragePath.create raw with
    | Error BackslashSeparator -> ()
    | other -> failwith $"'%s{raw}' should be refused for backslash, got %A{other}"

[<Theory>]
[<InlineData("C:/vigila")>]
[<InlineData("c:vigila")>]
[<InlineData("//server/share")>]
let ``a rooted or drive-qualified path is refused`` (raw: string) =
    // Stripping the prefix would silently redirect writes elsewhere, so these
    // are refused rather than normalised.
    match StoragePath.create raw with
    | Error(AbsoluteOrRooted _) -> ()
    | Error other -> failwith $"'%s{raw}' refused for the wrong reason: %A{other}"
    | Ok path -> failwith $"'%s{raw}' should be refused, got '%s{StoragePath.value path}'"

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("/")>]
[<InlineData("/ / ")>]
let ``an empty path is refused`` (raw: string) =
    Assert.True(Result.isError (StoragePath.create raw))

[<Fact>]
let ``a null path is refused rather than throwing`` () =
    Assert.True(Result.isError (StoragePath.create null))

[<Fact>]
let ``a control character is refused`` () =
    Assert.True(Result.isError (StoragePath.create "vigila\u0000items"))
    Assert.True(Result.isError (StoragePath.create "vigila\nitems"))

[<Theory>]
[<InlineData("vigila", "vigila")>]
[<InlineData("/vigila", "vigila")>]
[<InlineData("vigila/", "vigila")>]
[<InlineData("/vigila/", "vigila")>]
[<InlineData("vigila//data//", "vigila/data")>]
[<InlineData("teams/echelon/vigila", "teams/echelon/vigila")>]
let ``separator spelling is normalised without changing meaning`` (raw: string) (expected: string) =
    // The requirements write the default as "/vigila"; that is the same
    // repository-relative location as "vigila".
    Assert.Equal(expected, StoragePath.value (ok (StoragePath.create raw)))

[<Fact>]
let ``the documented default is a valid path`` () =
    // VIG-PER-004.
    Assert.Equal("vigila", StoragePath.value StoragePath.defaultPath)

// ---------------------------------------------------------------------------
// Layout. ADR-0002.
// ---------------------------------------------------------------------------

[<Fact>]
let ``the manifest sits at the storage root, not inside a workspace`` () =
    // VIG-PER-013: "does this area belong to Vigila?" is answered before any
    // workspace is known.
    Assert.Equal("vigila/manifest.json", manifestPath root)

[<Fact>]
let ``a workspace is keyed on its id, never its display name`` () =
    // VIG-PER-007: identity survives a rename, so no human-readable name may
    // appear in the path.
    let workspace = WorkspaceId.create ()
    let path = workspacePath root workspace

    Assert.Equal($"vigila/workspaces/%s{workspace.Segment}", path)
    Assert.DoesNotContain("-", workspace.Segment)

[<Fact>]
let ``workspace metadata is a separate file from the application manifest`` () =
    let workspace = WorkspaceId.create ()
    Assert.NotEqual<string>(manifestPath root, workspaceMetadataPath root workspace)

[<Fact>]
let ``an item is one file named by its immutable id`` () =
    // VIG-PER-041, VIG-PER-043, VIG-PER-044.
    let workspace = WorkspaceId.create ()
    let item = ItemId.create ()

    Assert.Equal(
        $"vigila/workspaces/%s{workspace.Segment}/items/%s{item.Segment}.json",
        itemPath root workspace item
    )

[<Fact>]
let ``item paths are stable across repeated computation`` () =
    let workspace = WorkspaceId.create ()
    let item = ItemId.create ()
    Assert.Equal(itemPath root workspace item, itemPath root workspace item)

[<Fact>]
let ``distinct items never share a path`` () =
    let workspace = WorkspaceId.create ()

    let paths =
        List.init 500 (fun _ -> itemPath root workspace (ItemId.create ()))

    Assert.Equal(500, paths |> List.distinct |> List.length)

[<Fact>]
let ``workspaces do not collide with each other`` () =
    let item = ItemId.create ()
    let a = WorkspaceId.create ()
    let b = WorkspaceId.create ()
    Assert.NotEqual<string>(itemPath root a item, itemPath root b item)

// ---------------------------------------------------------------------------
// The containment invariant. VIG-PER-005.
// ---------------------------------------------------------------------------

[<Fact>]
let ``every path this module produces is inside the storage area`` () =
    // The property that matters: no computed path can escape the configured
    // area, whatever the ids happen to be.
    for _ in 1..200 do
        let workspace = WorkspaceId.create ()
        let item = ItemId.create ()

        for path in
            [ manifestPath root
              workspacesPath root
              workspacePath root workspace
              workspaceMetadataPath root workspace
              itemsPath root workspace
              itemPath root workspace item ] do
            Assert.True(isInside root path, $"'%s{path}' escaped the storage area.")

[<Theory>]
[<InlineData("chrona/items/x.json")>]
[<InlineData("vigilax/items/x.json")>]
[<InlineData("/vigila/items/x.json")>]
[<InlineData("")>]
let ``a path outside the storage area is not reported as inside`` (candidate: string) =
    // "vigilax" is the case a naive prefix check gets wrong.
    Assert.False(isInside root candidate)

[<Fact>]
let ``a null candidate is not reported as inside`` () =
    Assert.False(isInside root null)

[<Fact>]
let ``the storage root itself counts as inside`` () =
    Assert.True(isInside root "vigila")

[<Fact>]
let ``a deeper storage path still contains its own paths`` () =
    let nested = ok (StoragePath.create "teams/echelon/vigila")
    let workspace = WorkspaceId.create ()
    let item = ItemId.create ()
    let path = itemPath nested workspace item

    Assert.StartsWith("teams/echelon/vigila/", path)
    Assert.True(isInside nested path)
    Assert.False(isInside root path)

// ===========================================================================
// Item serialization. VIG-PER-020/021/022, VIG-TIME-016, VIG-TST-014.
// ===========================================================================

module ItemSerialization =

    open System
    open System.Text.Json
    open Vigila.Semantic.Time
    open Vigila.Semantic.Actors
    open Vigila.Semantic.Items
    open Vigila.Semantic.Tags
    open Vigila.Semantic.Notes
    open Vigila.Semantic.History
    open Vigila.Semantic.Item
    open Vigila.Host.GitHub.ItemJson

    let private at text =
        match Instant.parse text with
        | Ok i -> i
        | Error e -> failwith e

    let private clock = Clock.fixedAt (at "2026-09-18T09:30:00.0000000-04:00")

    let private author =
        match Actor.human "Kevin" with
        | Ok a -> a
        | Error e -> failwith e

    let private titled text =
        match Title.create text with
        | Ok t -> t
        | Error e -> failwith e

    let private newItem () =
        Item.create clock author CreatedVia.UI (titled "Call accountant about generator")

    let private roundTrip item =
        match toJson item |> fromJson with
        | Ok restored -> restored
        | Error e -> failwith $"round trip failed: %s{e}"

    [<Fact>]
    let ``a freshly captured item round-trips unchanged`` () =
        let original = newItem ()
        Assert.Equal(original, roundTrip original)

    [<Fact>]
    let ``a fully populated item round-trips unchanged`` () =
        let tag t =
            match Tag.create t with
            | Ok v -> v
            | Error e -> failwith e

        // The note must carry the id of the item it hangs on. A note's ItemId
        // is not persisted separately -- it is implied by containment -- so the
        // format cannot represent a note attached to the wrong item, and a
        // round trip re-derives it from the parent.
        let baseItem = newItem ()

        let note =
            match Note.create clock author baseItem.Id "He asked for the cancelled check." with
            | Ok n -> n
            | Error e -> failwith e

        let original =
            { baseItem with
                Kind = FollowUp
                Status = ItemStatus.Waiting
                Description = Some "Generator documentation for the 2026 return."
                NextAction = Some "Send cancelled check"
                Due = Some(OnDate(DateOnly(2026, 9, 30)))
                FollowUp = Some(AtInstant(at "2026-09-22T14:00:00.0000000-04:00"))
                SnoozedUntil = Some(at "2026-09-20T08:00:00.0000000-04:00")
                WaitingOn = Some "CPA"
                WaitingSince = Some(at "2026-09-18T09:30:00.0000000-04:00")
                Tags = Set.ofList [ tag "tax"; tag "dad" ]
                Notes = [ note ]
                Sources =
                    [ { Type = "email"
                        DisplayName = "Re: generator"
                        ExternalId = Some "msg-123"
                        Url = Some "https://example.test/msg-123" } ]
                Important = true
                NeedsReview = true
                Resolution = Some Superseded
                ResolutionNote = Some "Replaced by the 2027 filing."
                CompletedAt = Some(at "2026-09-25T10:00:00.0000000-04:00")
                CancelledAt = None }

        Assert.Equal(original, roundTrip original)

    [<Fact>]
    let ``every history operation round-trips`` () =
        // Each case carries different detail; a case whose payload is dropped
        // on write or misread on read shows up here rather than in production.
        let tag =
            match Tag.create "tax" with
            | Ok v -> v
            | Error e -> failwith e

        let operations =
            [ Created
              TitleChanged("old", "new")
              DescriptionChanged
              StatusChanged(Open, Completed)
              KindChanged(Task, FollowUp)
              NextActionChanged(Some "before", Some "after")
              NextActionChanged(None, Some "after")
              DueDateChanged
              FollowUpDateChanged
              WaitingOnChanged(Some "CPA", None)
              Snoozed(at "2026-09-20T08:00:00.0000000-04:00")
              SnoozeCleared
              TagAdded tag
              TagRemoved tag
              NoteAdded
              ImportanceChanged true
              ReviewFlagChanged true ]

        let original =
            { newItem () with
                History = operations |> List.map (fun op -> History.entry (at "2026-09-18T09:30:00.0000000-04:00") author op) }

        Assert.Equal<HistoryOperation list>(
            operations,
            (roundTrip original).History |> List.map (fun e -> e.Operation)
        )

    // --- VIG-TIME-016: a date-only value must not shift -------------------

    [<Theory>]
    [<InlineData("UTC")>]
    [<InlineData("America/New_York")>]
    [<InlineData("Pacific/Kiritimati")>]
    [<InlineData("Pacific/Niue")>]
    [<InlineData("Asia/Kathmandu")>]
    let ``a date-only due value survives any reader time zone`` (zoneId: string) =
        // Kiritimati is UTC+14 and Niue UTC-11 -- the extremes where a
        // midnight-timestamp representation lands on the wrong calendar day.
        // Kathmandu is UTC+05:45, catching a half-hour-offset assumption.
        let zone = TimeZoneInfo.FindSystemTimeZoneById zoneId
        let day = DateOnly(2026, 9, 30)
        let original = { newItem () with Due = Some(OnDate day) }

        match (roundTrip original).Due with
        | Some(OnDate restored) ->
            Assert.Equal(day, restored)
            Assert.Equal(day, WhenValue.dayIn zone (OnDate restored))
        | other -> failwith $"expected a date-only value, got %A{other}"

    [<Fact>]
    let ``a date-only value is persisted without a time or an offset`` () =
        // The representation is what makes the property above hold, so it is
        // asserted directly rather than only through behaviour.
        let json = toJson { newItem () with Due = Some(OnDate(DateOnly(2026, 9, 30))) }
        use doc = JsonDocument.Parse json
        let due = doc.RootElement.GetProperty "due"

        Assert.Equal("date", due.GetProperty("kind").GetString())
        Assert.Equal("2026-09-30", due.GetProperty("value").GetString())

    [<Fact>]
    let ``an instant and a date are distinguishable after a round trip`` () =
        let asDate = { newItem () with Due = Some(OnDate(DateOnly(2026, 9, 30))) }
        let asInstant = { newItem () with Due = Some(AtInstant(at "2026-09-30T00:00:00.0000000Z")) }

        match (roundTrip asDate).Due, (roundTrip asInstant).Due with
        | Some(OnDate _), Some(AtInstant _) -> ()
        | a, b -> failwith $"the two forms collapsed: %A{a} and %A{b}"

    // --- schema versioning ------------------------------------------------

    [<Fact>]
    let ``every record carries its schema version`` () =
        // VIG-PER-020.
        use doc = JsonDocument.Parse(toJson (newItem ()))
        Assert.Equal(CurrentSchemaVersion, doc.RootElement.GetProperty("schemaVersion").GetInt32())

    [<Fact>]
    let ``a record from a newer schema is refused, not guessed at`` () =
        // VIG-PER-014, VIG-PER-022: forward tolerance never extends to a
        // version this build does not understand.
        let json =
            (toJson (newItem ())).Replace(
                $"\"schemaVersion\": {CurrentSchemaVersion}",
                $"\"schemaVersion\": {CurrentSchemaVersion + 1}"
            )

        match fromJson json with
        | Error msg -> Assert.Contains("schema version", msg)
        | Ok _ -> failwith "a newer schema version should be refused"

    [<Fact>]
    let ``a record with no schema version is refused`` () =
        match fromJson """{"id":"3f2a1c4e-0000-0000-0000-000000000000","title":"x"}""" with
        | Error _ -> ()
        | Ok _ -> failwith "a record without a schema version should be refused"

    [<Fact>]
    let ``an unknown field is ignored rather than rejected`` () =
        // VIG-PER-022: a non-breaking field added by a newer writer must not
        // break an older reader.
        let original = newItem ()
        let json = (toJson original).TrimEnd().TrimEnd('}') + ""","somethingNewerAdded": {"nested": [1, 2, 3]}}"""

        match fromJson json with
        | Ok restored -> Assert.Equal(original, restored)
        | Error e -> failwith $"an unknown field should be ignored: %s{e}"

    [<Fact>]
    let ``an unknown value in a closed set is refused`` () =
        // The other half of VIG-PER-022: tolerance applies to unknown *fields*,
        // never to unknown members of a closed domain set, where guessing would
        // fabricate state.
        for field, bogus in [ "kind", "reminder"; "status", "archived" ] do
            let json = (toJson (newItem ())).Replace($"\"{field}\": \"task\"", $"\"{field}\": \"{bogus}\"")
                                            .Replace($"\"{field}\": \"open\"", $"\"{field}\": \"{bogus}\"")

            match fromJson json with
            | Error _ -> ()
            | Ok _ -> failwith $"'%s{bogus}' should not be accepted as a %s{field}"

    [<Fact>]
    let ``malformed json is reported rather than thrown`` () =
        // VIG-PER-030: a malformed record is identified, not raised as an
        // unhandled exception that would stop other items loading.
        match fromJson "{ this is not json" with
        | Error msg -> Assert.NotEmpty msg
        | Ok _ -> failwith "malformed JSON should be refused"

    // --- capture behaviour -------------------------------------------------

    [<Fact>]
    let ``capturing an item needs only a title`` () =
        // VIG-DOM-004, VIG-UI-010.
        let item = newItem ()
        Assert.Equal(Task, item.Kind)
        Assert.Equal(Open, item.Status)
        Assert.Empty item.Tags
        Assert.Empty item.Notes
        Assert.True item.Due.IsNone
        Assert.False item.Important

    [<Fact>]
    let ``a captured item records its creation in history`` () =
        // VIG-DOM-032.
        let item = newItem ()
        Assert.Equal<HistoryOperation list>([ Created ], item.History |> List.map (fun e -> e.Operation))

    [<Fact>]
    let ``timestamps come from the supplied clock`` () =
        // VIG-TIME-023: no ambient time, which is what makes this assertable.
        let moment = at "2026-01-02T03:04:05.0000000Z"
        let item = Item.create (Clock.fixedAt moment) author CreatedVia.Agent (titled "x")

        Assert.Equal(moment, item.CreatedAt)
        Assert.Equal(moment, item.UpdatedAt)
        Assert.Equal(moment, item.LastActivityAt)

    [<Fact>]
    let ``snoozing leaves the due date untouched`` () =
        // VIG-TIME-012: an overdue item stays overdue while snoozed.
        let due = OnDate(DateOnly(2026, 9, 1))
        let item = { newItem () with Due = Some due }
        let snoozed = Item.snoozeUntil clock author (at "2026-10-01T00:00:00.0000000Z") item

        Assert.Equal<WhenValue option>(Some due, snoozed.Due)
        Assert.Equal(item.Status, snoozed.Status)

    [<Fact>]
    let ``adding the same tag twice changes nothing the second time`` () =
        // VIG-DOM-029: duplicates are not stored.
        let tag =
            match Tag.create "tax" with
            | Ok v -> v
            | Error e -> failwith e

        let once = Item.addTag clock author tag (newItem ())
        let twice = Item.addTag clock author tag once

        Assert.Equal(1, Set.count twice.Tags)
        Assert.Equal(once.History.Length, twice.History.Length)
