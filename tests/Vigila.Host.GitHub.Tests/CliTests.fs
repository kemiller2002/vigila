module Vigila.Host.GitHub.CliTests

open System
open System.Collections.Concurrent
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Xunit
open Vigila.Semantic.Time

type private RepositoryHandler() =
    inherit HttpMessageHandler()

    let files = ConcurrentDictionary<string, string>()
    let requests = ResizeArray<string * string * string>()

    member _.Files = files
    member _.Requests = requests |> Seq.toList

    override _.SendAsync(request, _cancellationToken) =
        let uri = request.RequestUri |> Option.ofObj |> Option.defaultWith (fun () -> failwith "Request URI is required.")
        let body =
            match request.Content with
            | null -> ""
            | content -> content.ReadAsStringAsync().GetAwaiter().GetResult()

        requests.Add(request.Method.Method, uri.AbsoluteUri, body)

        let result =
            if uri.AbsolutePath = "/repos/acme/vigila" then
                new HttpResponseMessage(HttpStatusCode.OK)
            else
                let marker = "/contents/"
                let index = uri.AbsolutePath.IndexOf(marker, StringComparison.Ordinal)
                let path = Uri.UnescapeDataString(uri.AbsolutePath.Substring(index + marker.Length))

                if request.Method = HttpMethod.Put then
                    use document = JsonDocument.Parse body
                    let encoded = document.RootElement.GetProperty("content").GetString() |> Option.ofObj |> Option.defaultValue ""
                    let text = Encoding.UTF8.GetString(Convert.FromBase64String encoded)

                    if files.TryAdd(path, text) then
                        new HttpResponseMessage(HttpStatusCode.Created)
                    else
                        new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                else
                    match files.TryGetValue path with
                    | true, text ->
                        let encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes text)
                        let response = new HttpResponseMessage(HttpStatusCode.OK)
                        response.Content <- new StringContent($"""{{"encoding":"base64","content":"%s{encoded}"}}""")
                        response
                    | _ -> new HttpResponseMessage(HttpStatusCode.NotFound)

        Task.FromResult result

let private instant = Instant.ofDateTimeOffset(DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero))
let private clock = Clock.fixedAt instant
let private workspace = "3f2a1c4e-0000-4000-8000-000000000001"

let private invocation title =
    $$"""
    {
      "capability": "followup.create",
      "contractVersion": 1,
      "envelope": {
        "schema": "echelon.execution-envelope/v1",
        "operationId": "op-cli",
        "correlationId": "corr-cli",
        "timestamp": "2026-09-26T12:00:00Z",
        "actor": {
          "kind": "agent",
          "provider": { "state": "known", "value": "openai" },
          "identity": { "state": "known", "value": "agent" },
          "runId": { "state": "unknown" },
          "sessionId": { "state": "unknown" }
        }
      },
      "payload": {
        "title": "{{title}}",
        "reason": "Provider contract test",
        "requestedAction": "review",
        "priority": "normal",
        "tags": ["contract"]
      }
    }
    """

let private defaultEnv (name: string) : string | null =
    if name = "VIGILA_GITHUB_TOKEN" then "ghs_test_secret" else null

let private runWithArguments handler getEnv input extraArguments =
    use http = new HttpClient(handler)
    use stdin = new StringReader(input)
    use stdout = new StringWriter()

    let arguments =
        [ "follow-up"; "add"
          "--repository"; "acme/vigila"
          "--branch"; "provider-data"
          "--workspace"; workspace
          "--storage-root"; "custom-vigila" ]
        @ extraArguments
        |> List.toArray

    let exitCode =
        Vigila.Cli.Program.runWith
            { Http = http
              GetEnv = getEnv
              Clock = clock
              Ids = Vigila.Semantic.Identifiers.IdSource.create Guid.NewGuid
              RetryPolicy = Vigila.Host.GitHub.RetryingRepositoryFiles.RetryPolicy.standard
              Wait = ignore
              Stdin = stdin
              Stdout = stdout }
            arguments

    exitCode, stdout.ToString()

let private run handler input =
    runWithArguments handler defaultEnv input [ "--stdin" ]

[<Fact>]
let cli_end_to_end_created () =
    let handler = new RepositoryHandler()
    let exitCode, output = run handler (invocation "Review provider")

    Assert.Equal(0, exitCode)
    use receipt = JsonDocument.Parse output
    Assert.Equal("created", receipt.RootElement.GetProperty("status").GetString())
    Assert.Equal("op-cli", receipt.RootElement.GetProperty("operationId").GetString())
    Assert.Equal(2, handler.Files.Count)
    Assert.Contains(handler.Files.Keys, fun p -> p.Contains("/operations/"))
    Assert.Contains(handler.Files.Keys, fun p -> p.Contains("/items/"))
    Assert.All(handler.Files.Keys, fun p -> Assert.StartsWith("custom-vigila/", p))
    Assert.DoesNotContain("ghs_test_secret", output)

    let putBodies =
        handler.Requests
        |> List.filter (fun (methodName, _, _) -> methodName = "PUT")
        |> List.map (fun (_, uri, body) -> uri, body)

    Assert.All(putBodies, fun (uri, body) ->
        Assert.Contains("/repos/acme/vigila/", uri)
        Assert.Contains("\"branch\":\"provider-data\"", body))

[<Fact>]
let cli_stdin_and_input_are_equivalent () =
    let json = invocation "Equivalent input"
    let stdinHandler = new RepositoryHandler()
    let inputHandler = new RepositoryHandler()
    let stdinExit, stdinOutput = run stdinHandler json
    let path = Path.GetTempFileName()

    try
        File.WriteAllText(path, json)

        let inputExit, inputOutput =
            runWithArguments inputHandler defaultEnv "" [ "--input"; path ]

        use stdinReceipt = JsonDocument.Parse stdinOutput
        use inputReceipt = JsonDocument.Parse inputOutput
        Assert.Equal(stdinExit, inputExit)
        Assert.Equal("created", stdinReceipt.RootElement.GetProperty("status").GetString())
        Assert.Equal("created", inputReceipt.RootElement.GetProperty("status").GetString())
        Assert.Equal(
            stdinReceipt.RootElement.GetProperty("code").GetString(),
            inputReceipt.RootElement.GetProperty("code").GetString()
        )
        Assert.Equal(stdinHandler.Files.Count, inputHandler.Files.Count)
        Assert.Equal<string list>(
            stdinHandler.Requests |> List.map (fun (methodName, _, _) -> methodName),
            inputHandler.Requests |> List.map (fun (methodName, _, _) -> methodName)
        )
    finally
        File.Delete path

[<Fact>]
let cli_uses_configured_token_environment () =
    let handler = new RepositoryHandler()
    let getEnv (name: string) : string | null = if name = "CUSTOM_VIGILA_TOKEN" then "custom-secret" else null

    let exitCode, output =
        runWithArguments
            handler
            getEnv
            (invocation "Configured credential")
            [ "--token-env"; "CUSTOM_VIGILA_TOKEN"; "--stdin" ]

    Assert.Equal(0, exitCode)
    Assert.DoesNotContain("custom-secret", output)

[<Theory>]
[<InlineData("VIGILA_GITHUB_TOKEN")>]
[<InlineData("GITHUB_TOKEN")>]
[<InlineData("GH_TOKEN")>]
let cli_uses_documented_default_token_environments (configuredName: string) =
    let handler = new RepositoryHandler()
    let getEnv (name: string) : string | null = if name = configuredName then "default-secret" else null
    let exitCode, output = runWithArguments handler getEnv (invocation "Default credential") [ "--stdin" ]

    Assert.Equal(0, exitCode)
    Assert.DoesNotContain("default-secret", output)

[<Fact>]
let cli_replay_and_conflict () =
    let handler = new RepositoryHandler()
    let firstExit, _ = run handler (invocation "Review provider")
    let replayExit, replay = run handler (invocation "Review provider")
    let conflictExit, conflict = run handler (invocation "Different request")

    Assert.Equal(0, firstExit)
    Assert.Equal(0, replayExit)
    Assert.Equal(3, conflictExit)
    Assert.Equal(2, handler.Files.Count)

    use replayReceipt = JsonDocument.Parse replay
    use conflictReceipt = JsonDocument.Parse conflict
    Assert.Equal("existing", replayReceipt.RootElement.GetProperty("status").GetString())
    Assert.Equal("conflict", conflictReceipt.RootElement.GetProperty("status").GetString())

[<Theory>]
[<InlineData("{ not json")>]
[<InlineData("")>]
let cli_rejects_bad_input (input: string) =
    let handler = new RepositoryHandler()
    let actual = if input = "" then invocation "" else input
    let exitCode, output = run handler actual

    Assert.Equal(2, exitCode)
    use receipt = JsonDocument.Parse output
    Assert.Equal("rejected", receipt.RootElement.GetProperty("status").GetString())
    Assert.Empty(handler.Files)

[<Fact>]
let cli_operational_failure () =
    let handler =
        { new HttpMessageHandler() with
            override _.SendAsync(_, _) = Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)) }

    let exitCode, output = run handler (invocation "Review provider")
    Assert.Equal(4, exitCode)
    use receipt = JsonDocument.Parse output
    Assert.Equal("failed", receipt.RootElement.GetProperty("status").GetString())
    Assert.DoesNotContain("ghs_test_secret", output)

// ---------------------------------------------------------------------------
// Typed exit codes (Echelon VIG-F2) and terminal failures (VIG-F3).
// ---------------------------------------------------------------------------

type private StatusHandler(status: HttpStatusCode) =
    inherit HttpMessageHandler()
    let mutable puts = 0
    member _.Puts = puts

    override _.SendAsync(request, _) =
        if request.Method = HttpMethod.Put then
            Threading.Interlocked.Increment(&puts) |> ignore

        Task.FromResult(new HttpResponseMessage(status))

[<Theory>]
[<InlineData(400)>]
[<InlineData(410)>]
let cli_permanent_client_error_is_terminal_and_sent_once (status: int) =
    let handler = new StatusHandler(enum<HttpStatusCode> status)
    let exitCode, output = run handler (invocation "Review provider")

    Assert.Equal(4, exitCode)
    use receipt = JsonDocument.Parse output
    Assert.Equal("failed", receipt.RootElement.GetProperty("status").GetString())
    Assert.False(receipt.RootElement.GetProperty("retryable").GetBoolean())
    Assert.Equal(1, handler.Puts)

[<Fact>]
let cli_persistent_server_error_exhausts_retries_then_is_terminal () =
    let handler = new StatusHandler(HttpStatusCode.ServiceUnavailable)
    let exitCode, output = run handler (invocation "Review provider")

    Assert.Equal(4, exitCode)
    use receipt = JsonDocument.Parse output
    Assert.False(receipt.RootElement.GetProperty("retryable").GetBoolean())
    Assert.Equal(Vigila.Host.GitHub.RetryingRepositoryFiles.RetryPolicy.standard.Attempts, handler.Puts)

/// An atomic in-memory ledger, so every CreateOutcome case can be produced
/// without HTTP and the exit-code mapping checked against typed values.
type private MemoryLedger() =
    let records = ConcurrentDictionary<string, Vigila.Application.Integration.FollowUpRecord>()

    interface Vigila.Application.Integration.FollowUpLedger with
        member _.Record record =
            let winner = records.GetOrAdd(record.Envelope.OperationId, record)

            if obj.ReferenceEquals(winner, record) then
                Ok Vigila.Application.Integration.Recorded
            else
                Ok(Vigila.Application.Integration.AlreadyRecorded winner)

[<Fact>]
let cli_exit_code_is_derived_from_the_typed_outcome () =
    let aegis =
        match Aegis.Bootstrap.validate None (Aegis.Aegis.configure "Vigila" None [ Aegis.Sinks.standardError ]) with
        | Ok valid -> valid
        | Result.Error problems -> failwith $"%A{problems}"

    let ids = Vigila.Semantic.Identifiers.IdSource.create Guid.NewGuid
    let execute ledger json = Vigila.Application.IntegrationWire.execute aegis clock ids ledger json
    let ledger = MemoryLedger()

    let failing =
        { new Vigila.Application.Integration.FollowUpLedger with
            member _.Record _ =
                Error
                    { Code = "RequestRejected"
                      Retryable = false
                      Detail = "RequestRejected" } }

    let exitOf = Vigila.Cli.Program.exitCodeOf
    Assert.Equal(0, exitOf (execute ledger (invocation "Typed")))
    Assert.Equal(0, exitOf (execute ledger (invocation "Typed")))
    Assert.Equal(3, exitOf (execute ledger (invocation "Different")))
    Assert.Equal(2, exitOf (execute ledger "{ not json"))
    Assert.Equal(4, exitOf (execute failing (invocation "Typed")))
