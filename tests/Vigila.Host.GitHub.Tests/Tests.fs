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
