/// Tier 4 - production GitHub-backed RepositoryFiles transport for VIG-15.
module Vigila.Host.GitHub.GitHubRepositoryFiles

open System
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Vigila.Host.GitHub.GitHubStore
open Vigila.Host.GitHub.FollowUpLedger

type GitHubRepositoryConfig =
    { Repository: string
      Branch: string }

/// Credentials are resolved only when an HTTP request is composed. This keeps
/// tokens at the infrastructure boundary and permits environment, GitHub App
/// installation-token, or future providers without changing the application.
type GitHubCredentialProvider = unit -> string option

let private endpoint (config: GitHubRepositoryConfig) path =
    let escapedPath =
        path.Split('/', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map Uri.EscapeDataString
        |> String.concat "/"

    $"https://api.github.com/repos/%s{config.Repository}/contents/%s{escapedPath}"

let private repositoryEndpoint (config: GitHubRepositoryConfig) =
    $"https://api.github.com/repos/%s{config.Repository}"

let private addHeaders (credential: GitHubCredentialProvider) (request: HttpRequestMessage) =
    request.Headers.UserAgent.ParseAdd("vigila-followup-provider/1")
    request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/vnd.github+json"))
    request.Headers.Add("X-GitHub-Api-Version", "2022-11-28")

    match credential () with
    | Some token when not (String.IsNullOrWhiteSpace token) ->
        request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)
    | _ -> ()

let private retryAfter (response: HttpResponseMessage) =
    match response.Headers.RetryAfter with
    | null -> None
    | value when value.Delta.HasValue -> Some(int value.Delta.Value.TotalSeconds)
    | _ -> None

let private isRateLimited (response: HttpResponseMessage) =
    response.StatusCode = HttpStatusCode.TooManyRequests
    || (response.StatusCode = HttpStatusCode.Forbidden
        && response.Headers.Contains("X-RateLimit-Remaining")
        && (response.Headers.GetValues("X-RateLimit-Remaining") |> Seq.exists ((=) "0")))

let private classify (response: HttpResponseMessage) =
    if isRateLimited response then
        RateLimited(retryAfter response)
    else
        match response.StatusCode with
        | HttpStatusCode.Unauthorized -> Unauthorized
        | HttpStatusCode.Forbidden -> Forbidden
        | HttpStatusCode.NotFound -> RepositoryNotFound
        | HttpStatusCode.Conflict
        | HttpStatusCode.UnprocessableEntity -> BranchUnavailable
        | _ -> RepositoryUnavailable

let private send (http: HttpClient) credential (request: HttpRequestMessage) =
    try
        addHeaders credential request
        Ok(http.SendAsync(request).GetAwaiter().GetResult())
    with
    | :? HttpRequestException
    | :? TaskCanceledException -> Error RepositoryUnavailable

let private verifyRepository (http: HttpClient) credential config =
    use request = new HttpRequestMessage(HttpMethod.Get, repositoryEndpoint config)

    match send http credential request with
    | Error failure -> Error failure
    | Ok response ->
        use response = response

        if response.IsSuccessStatusCode then Ok()
        else Error(classify response)

let private readContent (response: HttpResponseMessage) =
    try
        let json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        use document = JsonDocument.Parse json
        let root = document.RootElement

        match root.TryGetProperty "content", root.TryGetProperty "encoding" with
        | (true, content), (true, encoding)
            when content.ValueKind = JsonValueKind.String
                 && encoding.ValueKind = JsonValueKind.String
                 && String.Equals(encoding.GetString(), "base64", StringComparison.OrdinalIgnoreCase) ->
            let encoded = content.GetString() |> Option.ofObj |> Option.defaultValue ""
            Ok(Encoding.UTF8.GetString(Convert.FromBase64String(encoded.Replace("\n", ""))))
        | _ -> Error(StorageCorrupt "GitHub contents response")
    with
    | :? JsonException
    | :? FormatException -> Error(StorageCorrupt "GitHub contents response")

let private read (http: HttpClient) credential config path =
    let uri = $"%s{endpoint config path}?ref=%s{Uri.EscapeDataString config.Branch}"
    use request = new HttpRequestMessage(HttpMethod.Get, uri)

    match send http credential request with
    | Error failure -> Error failure
    | Ok response ->
        use response = response

        if response.IsSuccessStatusCode then
            readContent response |> Result.map Some
        elif response.StatusCode = HttpStatusCode.NotFound then
            // GitHub uses 404 both for an absent path and for an invisible
            // repository. Verify repository visibility before calling it absent.
            verifyRepository http credential config |> Result.map (fun () -> None)
        else
            Error(classify response)

let private createBody branch content =
    JsonSerializer.Serialize(
        {| message = "Vigila: persist follow-up integration record"
           content = Convert.ToBase64String(Encoding.UTF8.GetBytes content)
           branch = branch |}
    )

let private existsAfterCreateFailure (http: HttpClient) credential config path =
    match read http credential config path with
    | Ok(Some _) -> Ok true
    | Ok None -> Ok false
    | Error failure -> Error failure

let private createNew (http: HttpClient) credential config path content =
    // Important: this is the first repository operation. No SHA is supplied,
    // so GitHub arbitrates create-if-absent atomically.
    use request = new HttpRequestMessage(HttpMethod.Put, endpoint config path)
    request.Content <- new StringContent(createBody config.Branch content, Encoding.UTF8, "application/json")

    match send http credential request with
    | Error failure -> Error failure
    | Ok response ->
        use response = response

        if response.StatusCode = HttpStatusCode.Created then
            Ok FileCreated
        elif response.StatusCode = HttpStatusCode.UnprocessableEntity
             || response.StatusCode = HttpStatusCode.Conflict then
            // This read occurs only after GitHub refused the create. It
            // classifies the refusal; it is not an exclusivity check.
            match existsAfterCreateFailure http credential config path with
            | Ok true -> Ok FileAlreadyExists
            | Ok false -> Error BranchUnavailable
            | Error failure -> Error failure
        else
            Error(classify response)

/// Production RepositoryFiles over GitHub's REST contents API. It never clones
/// or requires a target-repository working tree.
type GitHubFiles(http: HttpClient, config: GitHubRepositoryConfig, credential: GitHubCredentialProvider) =
    interface RepositoryFiles with
        member _.Read path = read http credential config path
        member _.CreateNew(path, content) = createNew http credential config path content

let create (http: HttpClient) config credential : RepositoryFiles =
    GitHubFiles(http, config, credential) :> RepositoryFiles
