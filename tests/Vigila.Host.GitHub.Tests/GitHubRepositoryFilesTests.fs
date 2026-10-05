module Vigila.Host.GitHub.GitHubRepositoryFilesTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Xunit
open Vigila.Host.GitHub.FollowUpLedger
open Vigila.Host.GitHub.GitHubStore
open Vigila.Host.GitHub.GitHubRepositoryFiles

type private FakeHandler(respond: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()

    let requests = ResizeArray<string * string * string option>()

    member _.Requests = requests |> Seq.toList

    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        let authorization =
            match request.Headers.Authorization with
            | null -> None
            | value -> value.Parameter |> Option.ofObj

        let uri = request.RequestUri |> Option.ofObj |> Option.defaultWith (fun () -> failwith "Request URI is required.")
        requests.Add(request.Method.Method, uri.AbsoluteUri, authorization)
        Task.FromResult(respond request)

let private response (status: HttpStatusCode) (body: string) =
    let r = new HttpResponseMessage(status)
    r.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    r

let private contentResponse (text: string) =
    let encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes text)
    response HttpStatusCode.OK ($"""{{"type":"file","encoding":"base64","content":"%s{encoded}"}}""")

let private config =
    { Repository = "acme/vigila"
      Branch = "integration-data" }

let private filesWith handler token =
    let http = new HttpClient(handler)
    create http config (fun () -> Some token)

[<Fact>]
let read_existing () =
    let handler =
        new FakeHandler(fun request ->
            let uri = request.RequestUri |> Option.ofObj |> Option.defaultWith (fun () -> failwith "Request URI is required.")
            Assert.Contains("/repos/acme/vigila/contents/vigila/workspaces/x.json", uri.AbsoluteUri)
            Assert.Contains("ref=integration-data", uri.Query)
            contentResponse "hello")

    let files = filesWith handler "secret-token"
    Assert.Equal(Ok(Some "hello"), files.Read "vigila/workspaces/x.json")

[<Fact>]
let read_missing () =
    let handler =
        new FakeHandler(fun request ->
            let uri = request.RequestUri |> Option.ofObj |> Option.defaultWith (fun () -> failwith "Request URI is required.")
            if uri.AbsolutePath.EndsWith("/repos/acme/vigila") then
                response HttpStatusCode.OK "{}"
            else
                response HttpStatusCode.NotFound "{}")

    let files = filesWith handler "secret-token"
    Assert.Equal(Ok None, files.Read "vigila/missing.json")
    Assert.Equal(2, handler.Requests.Length)

[<Fact>]
let read_inaccessible_repository () =
    let handler = new FakeHandler(fun _ -> response HttpStatusCode.NotFound "{}")
    let files = filesWith handler "secret-token"

    Assert.Equal(Error RepositoryNotFound, files.Read "vigila/missing.json")
    Assert.Equal(2, handler.Requests.Length)

[<Theory>]
[<InlineData(401, "Unauthorized")>]
[<InlineData(403, "Forbidden")>]
[<InlineData(500, "RepositoryUnavailable")>]
let read_failures (status: int, expected: string) =
    let handler = new FakeHandler(fun _ -> response (enum<HttpStatusCode> status) "{}")
    let files = filesWith handler "secret-token"

    match files.Read "vigila/x.json" with
    | Error failure -> Assert.Equal(expected, codeOf failure)
    | Ok value -> failwith $"Expected failure, got %A{value}"

[<Fact>]
let create_without_read () =
    let handler =
        new FakeHandler(fun request ->
            let content =
                request.Content
                |> Option.ofObj
                |> Option.defaultWith (fun () -> failwith "Create request content is required.")

            let body = content.ReadAsStringAsync().GetAwaiter().GetResult()
            use document = JsonDocument.Parse body
            Assert.Equal("integration-data", document.RootElement.GetProperty("branch").GetString())
            Assert.False(document.RootElement.TryGetProperty("sha") |> fst)
            response HttpStatusCode.Created "{}")
    let files = filesWith handler "secret-token"

    Assert.Equal(Ok FileCreated, files.CreateNew("vigila/new.json", "{}"))
    Assert.Single(handler.Requests) |> ignore
    let methodName, uri, _ = handler.Requests.Head
    Assert.Equal("PUT", methodName)
    Assert.Contains("/repos/acme/vigila/contents/vigila/new.json", uri)

[<Fact>]
let create_existing () =
    let handler =
        new FakeHandler(fun request ->
            if request.Method = HttpMethod.Put then response HttpStatusCode.UnprocessableEntity "{}"
            else contentResponse "existing")

    let files = filesWith handler "secret-token"
    Assert.Equal(Ok FileAlreadyExists, files.CreateNew("vigila/existing.json", "{}"))
    Assert.Equal<string list>([ "PUT"; "GET" ], handler.Requests |> List.map (fun (m, _, _) -> m))

[<Fact>]
let token_is_not_returned () =
    let token = "ghs_super_secret"
    let handler = new FakeHandler(fun _ -> response HttpStatusCode.Unauthorized "{}")
    let files = filesWith handler token

    let result = files.Read "vigila/x.json"
    Assert.Equal(Error Unauthorized, result)
    Assert.DoesNotContain(token, sprintf "%A" result)
    Assert.All(handler.Requests, fun (_, _, seen) -> Assert.Equal(Some token, seen))

// ---------------------------------------------------------------------------
// Explicit status classification (Echelon VIG-F3). Every status class has a
// stated, tested disposition; nothing falls through to "retryable".
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData(400, "RequestRejected", false)>]
[<InlineData(405, "RequestRejected", false)>]
[<InlineData(410, "RequestRejected", false)>]
[<InlineData(413, "RequestRejected", false)>]
[<InlineData(451, "RequestRejected", false)>]
[<InlineData(401, "Unauthorized", false)>]
[<InlineData(403, "Forbidden", false)>]
[<InlineData(404, "RepositoryNotFound", false)>]
[<InlineData(409, "BranchUnavailable", false)>]
[<InlineData(422, "BranchUnavailable", false)>]
[<InlineData(408, "RepositoryUnavailable", true)>]
[<InlineData(429, "RateLimited", true)>]
[<InlineData(500, "RepositoryUnavailable", true)>]
[<InlineData(502, "RepositoryUnavailable", true)>]
[<InlineData(503, "RepositoryUnavailable", true)>]
[<InlineData(504, "RepositoryUnavailable", true)>]
[<InlineData(302, "UnexpectedStatus", false)>]
let ``every status class has an explicit disposition`` (status: int, code: string, retryable: bool) =
    let failure = classifyStatus status false None
    Assert.Equal(code, codeOf failure)
    Assert.Equal(retryable, failure.IsRetryable)

[<Fact>]
let ``no 4xx other than 408 and 429 is retryable, and every 5xx is`` () =
    for status in 400..499 do
        let expected = status = 408 || status = 429
        Assert.True((classifyStatus status false None).IsRetryable = expected, $"status %d{status}")

    for status in 500..599 do
        Assert.True((classifyStatus status false None).IsRetryable, $"status %d{status}")

[<Fact>]
let ``a 403 with an exhausted rate limit is RateLimited, not Forbidden`` () =
    Assert.Equal(RateLimited(Some 7), classifyStatus 403 true (Some 7))
    Assert.Equal(Forbidden, classifyStatus 403 false None)

[<Theory>]
[<InlineData(400)>]
[<InlineData(410)>]
let ``a permanent client error over HTTP is terminal`` (status: int) =
    let handler = new FakeHandler(fun _ -> response (enum<HttpStatusCode> status) "{}")
    let files = filesWith handler "secret-token"

    match files.Read "vigila/x.json" with
    | Error failure ->
        Assert.Equal(RequestRejected status, failure)
        Assert.False(failure.IsRetryable)
    | Ok value -> failwith $"Expected failure, got %A{value}"

[<Fact>]
let ``a terminal create failure is not presented as retryable to the ledger`` () =
    let handler = new FakeHandler(fun _ -> response HttpStatusCode.BadRequest "{}")
    let files = filesWith handler "secret-token"

    match files.CreateNew("vigila/new.json", "{}") with
    | Error failure ->
        let ledgerFailure = failureOf failure
        Assert.Equal("RequestRejected", ledgerFailure.Code)
        Assert.False(ledgerFailure.Retryable)
    | Ok value -> failwith $"Expected failure, got %A{value}"
