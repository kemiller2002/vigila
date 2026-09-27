module Vigila.Cli.Program

open System
open System.IO
open System.Net.Http
open System.Text.Json
open Aegis
open Vigila.Semantic.Identifiers
open Vigila.Semantic.Time
open Vigila.Application
open Vigila.Host.GitHub
open Vigila.Host.GitHub.StorageLayout

type private Options =
    { Repository: string option
      Branch: string
      Workspace: string option
      StorageRoot: string
      TokenEnvironment: string option
      InputFile: string option
      Stdin: bool }

let private defaults =
    { Repository = None
      Branch = "main"
      Workspace = None
      StorageRoot = StoragePath.Default
      TokenEnvironment = None
      InputFile = None
      Stdin = false }

let private parse args =
    let rec loop options remaining =
        match remaining with
        | [] -> Ok options
        | "--repository" :: value :: tail -> loop { options with Repository = Some value } tail
        | "--branch" :: value :: tail -> loop { options with Branch = value } tail
        | "--workspace" :: value :: tail -> loop { options with Workspace = Some value } tail
        | "--storage-root" :: value :: tail -> loop { options with StorageRoot = value } tail
        | "--token-env" :: value :: tail -> loop { options with TokenEnvironment = Some value } tail
        | "--input" :: value :: tail -> loop { options with InputFile = Some value } tail
        | "--stdin" :: tail -> loop { options with Stdin = true } tail
        | option :: _ -> Error $"Unknown or incomplete option '%s{option}'."

    match args |> Array.toList with
    | "follow-up" :: "add" :: rest -> loop defaults rest
    | _ -> Error "Usage: vigila follow-up add --repository OWNER/REPO --workspace ID [--branch BRANCH] [--storage-root PATH] (--input FILE | --stdin)"

let private machineError code message retryable =
    JsonSerializer.Serialize(
        {| status = "failed"
           code = code
           message = message
           retryable = retryable |}
    )

let private rejected code message =
    JsonSerializer.Serialize(
        {| status = "rejected"
           code = code
           errors = [| {| field = "cli"; message = message |} |]
           retryable = false |}
    )

let private tokenProvider getEnv tokenEnvironment =
    fun () ->
        match tokenEnvironment with
        | Some name -> getEnv name |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not)
        | None ->
            [ "VIGILA_GITHUB_TOKEN"; "GITHUB_TOKEN"; "GH_TOKEN" ]
            |> List.tryPick (fun name -> getEnv name |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not))

let private readInput (stdin: TextReader) options =
    match options.InputFile, options.Stdin with
    | Some path, false ->
        try Ok(File.ReadAllText path)
        with :? IOException -> Error "The input file could not be read."
    | None, true -> Ok(stdin.ReadToEnd())
    | Some _, true -> Error "Choose exactly one input source: --input or --stdin."
    | None, false -> Error "An input source is required: --input FILE or --stdin."

let private aegis () =
    match Bootstrap.validate None (Aegis.configure "Vigila" None [ Sinks.standardError ]) with
    | Ok valid -> Ok valid
    | Result.Error _ -> Error "Vigila diagnostics could not be initialized."

/// Injectable composition root used by tests. Semantic parsing and validation
/// remain exclusively in IntegrationWire.invoke.
let runWith
    (http: HttpClient)
    (getEnv: string -> string | null)
    (clock: Clock)
    (stdin: TextReader)
    (stdout: TextWriter)
    (args: string array)
    =
    let fail exitCode (json: string) =
        stdout.WriteLine json
        exitCode

    match parse args with
    | Error message -> fail 2 (rejected "CliArgumentsInvalid" message)
    | Ok options ->
        match options.Repository, options.Workspace with
        | None, _ -> fail 2 (rejected "CliArgumentsInvalid" "--repository is required.")
        | _, None -> fail 2 (rejected "CliArgumentsInvalid" "--workspace is required.")
        | Some repository, Some workspaceText ->
            match WorkspaceId.parse workspaceText, StoragePath.create options.StorageRoot, readInput stdin options, aegis () with
            | Error message, _, _, _ -> fail 2 (rejected "WorkspaceInvalid" message)
            | _, Error refusal, _, _ -> fail 2 (rejected "StorageRootInvalid" refusal.Describe)
            | _, _, Error message, _ -> fail 2 (rejected "InputInvalid" message)
            | _, _, _, Error message -> fail 4 (machineError "DiagnosticsUnavailable" message false)
            | Ok workspace, Ok root, Ok json, Ok diagnostics ->
                let credential = tokenProvider getEnv options.TokenEnvironment

                match credential () with
                | None -> fail 4 (machineError "MissingCredential" "No GitHub credential is configured." false)
                | Some _ ->
                    let files =
                        GitHubRepositoryFiles.create
                            http
                            { Repository = repository
                              Branch = options.Branch }
                            credential

                    let ledger = FollowUpLedger.create files root workspace
                    let receipt = IntegrationWire.invoke diagnostics clock ledger json
                    stdout.WriteLine receipt

                    use document = JsonDocument.Parse receipt
                    match document.RootElement.GetProperty("status").GetString() with
                    | "created"
                    | "existing" -> 0
                    | "rejected" -> 2
                    | "conflict" -> 3
                    | _ -> 4

[<EntryPoint>]
let main args =
    use http = new HttpClient()
    let clock = Clock.create (fun () -> Instant.ofDateTimeOffset DateTimeOffset.UtcNow)
    runWith http Environment.GetEnvironmentVariable clock Console.In Console.Out args
