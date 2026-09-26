/// Tier 4 - foreign values and provenance records as JSON text.
///
/// Tier 1 holds a foreign value as `Verbatim` and decides everything about a
/// provenance record (Vigila.Semantic.Provenance). This module only translates
/// between that value and JSON text, without losing anything on the way: object
/// members keep their order, numbers keep their source text, and a member name
/// that appears twice is refused rather than silently collapsed.
///
/// Requirements: VIG-PER-025, VIG-DOM-054, VIG-AGT-037.
module Vigila.Host.GitHub.ProvenanceJson

open System
open System.Text
open System.Text.Json
open Vigila.Semantic.Carried
open Vigila.Semantic.Provenance

[<RequireQualifiedAccess>]
module CarriedJson =

    /// Reads a JSON element into a verbatim value.
    let rec ofElement (element: JsonElement) : Result<Verbatim, string> =
        match element.ValueKind with
        | JsonValueKind.Object ->
            let members = element.EnumerateObject() |> Seq.toList
            let names = members |> List.map _.Name

            match names |> List.countBy id |> List.tryFind (snd >> (<) 1) with
            | Some(name, _) -> Error $"'%s{name}' appears more than once in one object."
            | None ->
                members
                |> List.fold
                    (fun acc item ->
                        match acc, ofElement item.Value with
                        | Error message, _ -> Error message
                        | Ok _, Error message -> Error message
                        | Ok items, Ok value -> Ok((item.Name, value) :: items))
                    (Ok [])
                |> Result.map (List.rev >> Verbatim.Object)
        | JsonValueKind.Array ->
            element.EnumerateArray()
            |> Seq.toList
            |> List.fold
                (fun acc item ->
                    match acc, ofElement item with
                    | Error message, _ -> Error message
                    | Ok _, Error message -> Error message
                    | Ok items, Ok value -> Ok(value :: items))
                (Ok [])
            |> Result.map (List.rev >> Verbatim.Array)
        | JsonValueKind.String ->
            match element.GetString() with
            | NonNull text -> Ok(Verbatim.String text)
            | Null -> Error "A string value was null."
        | JsonValueKind.Number -> Ok(Verbatim.Number(element.GetRawText()))
        | JsonValueKind.True -> Ok(Verbatim.Bool true)
        | JsonValueKind.False -> Ok(Verbatim.Bool false)
        | JsonValueKind.Null -> Ok Verbatim.Null
        | other -> Error $"Unsupported JSON value kind %A{other}."

    /// Writes a verbatim value as the current JSON value.
    let rec write (writer: Utf8JsonWriter) (value: Verbatim) =
        match value with
        | Verbatim.Null -> writer.WriteNullValue()
        | Verbatim.Bool flag -> writer.WriteBooleanValue flag
        | Verbatim.Number raw -> writer.WriteRawValue(raw, skipInputValidation = false)
        | Verbatim.String text -> writer.WriteStringValue text
        | Verbatim.Array items ->
            writer.WriteStartArray()
            items |> List.iter (write writer)
            writer.WriteEndArray()
        | Verbatim.Object members ->
            writer.WriteStartObject()

            members
            |> List.iter (fun (name, item) ->
                writer.WritePropertyName name
                write writer item)

            writer.WriteEndObject()

    /// Writes a verbatim value as a named property of the current object.
    let writeProperty (writer: Utf8JsonWriter) (name: string) (value: Verbatim) =
        writer.WritePropertyName name
        write writer value

    let parse (text: string) : Result<Verbatim, string> =
        try
            use parsedDocument = JsonDocument.Parse text
            ofElement parsedDocument.RootElement
        with :? JsonException as ex ->
            Error $"not valid JSON: %s{ex.Message}"

    let render (indented: bool) (value: Verbatim) =
        use stream = new IO.MemoryStream()

        (use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = indented))
         write writer value)

        Encoding.UTF8.GetString(stream.ToArray())

/// The interchange codec as text (VIG-AGT-037): the rules are Tier 1's.
[<RequireQualifiedAccess>]
module ProvenanceRecordJson =

    let private asProblems message = [ { Field = ""; Message = message } ]

    /// Reads a record and applies every structural rule. Returns the value as
    /// held, and what was found: current, unversioned, or an unsupported major
    /// to carry verbatim.
    let parse (text: string) : Result<Verbatim * Reading, ProvenanceProblem list> =
        match CarriedJson.parse text with
        | Error message -> Error(asProblems message)
        | Ok value -> ProvenanceRecord.validate value |> Result.map (fun reading -> value, reading)

    let render (value: Verbatim) = CarriedJson.render true value
