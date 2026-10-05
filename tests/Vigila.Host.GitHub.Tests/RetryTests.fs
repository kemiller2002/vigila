/// Bounded retry (Echelon VIG-F3): retryable failures are retried a bounded
/// number of times, terminal failures are not retried at all, and exhausting
/// the budget yields an explicit terminal `RetriesExhausted`.
module Vigila.Host.GitHub.RetryTests

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Xunit
open Vigila.Host.GitHub.GitHubStore
open Vigila.Host.GitHub.GitHubRepositoryFiles
open Vigila.Host.GitHub.RetryingRepositoryFiles

let private policy =
    match RetryPolicy.create 3 (TimeSpan.FromMilliseconds 100.0) (TimeSpan.FromSeconds 1.0) with
    | Ok p -> p
    | Error e -> failwith e

/// Runs `run` with an operation that replays `results` in order, and records
/// every wait instead of sleeping.
let private replay (results: Result<string, GitHubFailure> list) =
    let calls = ref 0
    let waits = ResizeArray<TimeSpan>()

    let operation () =
        let index = calls.Value
        calls.Value <- index + 1
        results[min index (results.Length - 1)]

    let outcome = run policy waits.Add operation
    outcome, calls.Value, waits |> Seq.toList

[<Fact>]
let ``a terminal failure is returned on the first attempt without waiting`` () =
    let outcome, calls, waits = replay [ Error(RequestRejected 400) ]
    Assert.Equal(Error(RequestRejected 400), outcome)
    Assert.Equal(1, calls)
    Assert.Empty(waits)

[<Fact>]
let ``a transient failure that clears is retried to success`` () =
    let outcome, calls, waits = replay [ Error RepositoryUnavailable; Ok "done" ]
    Assert.Equal(Ok "done", outcome)
    Assert.Equal(2, calls)
    Assert.Equal<TimeSpan list>([ TimeSpan.FromMilliseconds 100.0 ], waits)

[<Fact>]
let ``exhausting retries yields an explicit terminal outcome`` () =
    let outcome, calls, waits = replay [ Error RepositoryUnavailable ]

    match outcome with
    | Error(RetriesExhausted(attempts, last) as failure) ->
        Assert.Equal(3, attempts)
        Assert.Equal(RepositoryUnavailable, last)
        Assert.False(failure.IsRetryable)
        Assert.Equal("RetriesExhausted", codeOf failure)
    | other -> failwith $"Expected RetriesExhausted, got %A{other}"

    Assert.Equal(3, calls)
    // Exponential backoff between the three attempts.
    Assert.Equal<TimeSpan list>([ TimeSpan.FromMilliseconds 100.0; TimeSpan.FromMilliseconds 200.0 ], waits)

[<Fact>]
let ``a rate limit honours retry-after, capped by the policy`` () =
    Assert.Equal(RetryAfter(TimeSpan.FromSeconds 1.0), decide policy 1 (RateLimited(Some 60)))
    Assert.Equal(RetryAfter(TimeSpan.FromMilliseconds 100.0), decide policy 1 (RateLimited None))

[<Fact>]
let ``the retry decision is pure and stops at the budget`` () =
    Assert.Equal(Stop(RequestRejected 410), decide policy 1 (RequestRejected 410))
    Assert.Equal(Stop(RetriesExhausted(3, RateLimited None)), decide policy 3 (RateLimited None))

[<Fact>]
let ``a policy needs at least one attempt and non-negative delays`` () =
    Assert.True(Result.isError (RetryPolicy.create 0 TimeSpan.Zero TimeSpan.Zero))
    Assert.True(Result.isError (RetryPolicy.create 1 (TimeSpan.FromSeconds -1.0) TimeSpan.Zero))
    Assert.Equal(3, RetryPolicy.standard.Attempts)

type private CountingHandler(status: HttpStatusCode) =
    inherit HttpMessageHandler()
    let mutable count = 0
    member _.Count = count

    override _.SendAsync(_request: HttpRequestMessage, _token: CancellationToken) =
        Interlocked.Increment(&count) |> ignore
        Task.FromResult(new HttpResponseMessage(status))

let private wrapped (handler: CountingHandler) =
    let http = new HttpClient(handler)

    create http { Repository = "acme/vigila"; Branch = "main" } (fun () -> Some "token")
    |> wrap policy ignore

[<Fact>]
let ``over HTTP a persistent 503 is attempted exactly the budget, then is terminal`` () =
    let handler = new CountingHandler(HttpStatusCode.ServiceUnavailable)

    match (wrapped handler).CreateNew("vigila/x.json", "{}") with
    | Error(RetriesExhausted(3, RepositoryUnavailable)) -> ()
    | other -> failwith $"Expected RetriesExhausted, got %A{other}"

    Assert.Equal(3, handler.Count)

[<Fact>]
let ``over HTTP a 400 is sent once and never retried`` () =
    let handler = new CountingHandler(HttpStatusCode.BadRequest)
    Assert.Equal(Error(RequestRejected 400), (wrapped handler).CreateNew("vigila/x.json", "{}"))
    Assert.Equal(1, handler.Count)
