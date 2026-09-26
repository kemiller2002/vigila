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

let private run handler input =
    use http = new HttpClient(handler)
    use stdin = new StringReader(input)
    use stdout = new StringWriter()
    let env (name: string) : string | null = if name = "VIGILA_GITHUB_TOKEN" then "ghs_test_secret" else null

    let exitCode =
        Vigila.Cli.Program.runWith
            http
            env
            clock
            stdin
            stdout
            [| "follow-up"; "add"
               "--repository"; "acme/vigila"
               "--branch"; "provider-data"
               "--workspace"; workspace
               "--storage-root"; "custom-vigila"
               "--stdin" |]

    exitCode, stdout.ToString()

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
    let handler = RepositoryHandler()
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
