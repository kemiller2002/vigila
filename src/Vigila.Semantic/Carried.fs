/// Tier 1 - a foreign value carried without interpretation.
///
/// Some data Vigila holds belongs to someone else: a Praxis provenance record,
/// or fields a newer writer put on an actor. Vigila must hand such data back
/// exactly as it received it (VIG-PER-025), including the parts it does not
/// understand. This type is the smallest shape that can do that without
/// depending on a serializer: Tier 1 must not know how anything is written
/// down (VIG-GOV-015), so Tier 4 translates to and from it.
///
/// Numbers keep their source text, and object members keep their order, so a
/// value that goes in comes out unchanged.
///
/// Requirements: VIG-PER-025, VIG-DOM-054.
module Vigila.Semantic.Carried

/// A structured value exactly as a foreign writer produced it.
[<RequireQualifiedAccess>]
type Verbatim =
    | Null
    | Bool of bool
    /// The number's source text, never re-formatted.
    | Number of string
    | String of string
    | Array of Verbatim list
    /// Members in source order.
    | Object of (string * Verbatim) list

[<RequireQualifiedAccess>]
module Verbatim =

    let field (name: string) value =
        match value with
        | Verbatim.Object members -> members |> List.tryFind (fst >> (=) name) |> Option.map snd
        | _ -> None

    let asString value =
        match value with
        | Verbatim.String text -> Some text
        | _ -> None

    let stringField name value = field name value |> Option.bind asString

    let has name value = field name value |> Option.isSome

    /// Replaces a member in place, or appends it when absent, so the order of
    /// every other member is kept.
    let setField (name: string) (member': Verbatim) value =
        match value with
        | Verbatim.Object members when members |> List.exists (fst >> (=) name) ->
            members
            |> List.map (fun (key, existing) -> if key = name then key, member' else key, existing)
            |> Verbatim.Object
        | Verbatim.Object members -> Verbatim.Object(members @ [ name, member' ])
        | other -> other

    let strings (values: string list) =
        values |> List.map Verbatim.String |> Verbatim.Array

    /// Equality of meaning: object member order is not significant, array
    /// order is. This is the comparison the provenance contract asks for when
    /// it says a record is carried "verbatim".
    let rec equivalent left right =
        match left, right with
        | Verbatim.Object a, Verbatim.Object b ->
            List.length a = List.length b
            && a
               |> List.forall (fun (key, value) ->
                   match b |> List.tryFind (fst >> (=) key) with
                   | Some(_, other) -> equivalent value other
                   | None -> false)
        | Verbatim.Array a, Verbatim.Array b -> List.length a = List.length b && List.forall2 equivalent a b
        | a, b -> a = b
