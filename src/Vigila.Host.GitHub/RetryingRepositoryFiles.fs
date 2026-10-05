/// Tier 4 - bounded retry around any `RepositoryFiles`.
///
/// Only failures whose `IsRetryable` is true are retried (408, 429, 5xx and
/// transport failures). Terminal failures (other 4xx, auth, not-found,
/// corruption) return on the first attempt. When the attempt budget runs out,
/// the result is `RetriesExhausted`, which is itself terminal, so no caller
/// loops forever on a condition that is not clearing.
///
/// Retrying `CreateNew` is safe because it is create-only: if an attempt that
/// reported a transport failure did in fact create the file, the next attempt
/// observes `FileAlreadyExists`, and the ledger then reports the operation as
/// already processed rather than creating anything twice (ADR-0004).
///
/// The decision is pure (`decide`). Waiting is an injected effect, so tests
/// never sleep and the composition root chooses the real delay.
module Vigila.Host.GitHub.RetryingRepositoryFiles

open System
open Vigila.Host.GitHub.GitHubStore

/// How many attempts to make in total, and how long to wait between them.
type RetryPolicy =
    private
        { MaxAttempts: int
          BaseDelay: TimeSpan
          MaxDelay: TimeSpan }

    member this.Attempts = this.MaxAttempts

[<RequireQualifiedAccess>]
module RetryPolicy =

    /// A policy with at least one attempt and non-negative delays.
    let create maxAttempts (baseDelay: TimeSpan) (maxDelay: TimeSpan) =
        if maxAttempts < 1 then
            Error "A retry policy needs at least one attempt."
        elif baseDelay < TimeSpan.Zero || maxDelay < TimeSpan.Zero then
            Error "Retry delays cannot be negative."
        else
            Ok
                { MaxAttempts = maxAttempts
                  BaseDelay = baseDelay
                  MaxDelay = maxDelay }

    /// Three attempts, 500 ms doubling, capped at 10 s per wait.
    let standard =
        { MaxAttempts = 3
          BaseDelay = TimeSpan.FromMilliseconds 500.0
          MaxDelay = TimeSpan.FromSeconds 10.0 }

/// What to do after a failed attempt.
type RetryDecision =
    | RetryAfter of TimeSpan
    | Stop of GitHubFailure

let private delayFor policy attempt failure =
    let backoff =
        TimeSpan.FromTicks(policy.BaseDelay.Ticks * (1L <<< min (attempt - 1) 20))

    let requested =
        match failure with
        | RateLimited(Some seconds) when seconds > 0 -> TimeSpan.FromSeconds(float seconds)
        | _ -> backoff

    if requested > policy.MaxDelay then policy.MaxDelay else requested

/// Pure retry decision for the failure observed on attempt `attempt` (1-based).
let decide (policy: RetryPolicy) (attempt: int) (failure: GitHubFailure) =
    if not failure.IsRetryable then Stop failure
    elif attempt >= policy.MaxAttempts then Stop(RetriesExhausted(attempt, failure))
    else RetryAfter(delayFor policy attempt failure)

/// Runs `operation` under `policy`, calling `wait` between attempts.
let run (policy: RetryPolicy) (wait: TimeSpan -> unit) (operation: unit -> Result<'T, GitHubFailure>) =
    let rec attemptFrom attempt =
        match operation () with
        | Ok value -> Ok value
        | Error failure ->
            match decide policy attempt failure with
            | Stop terminal -> Error terminal
            | RetryAfter delay ->
                wait delay
                attemptFrom (attempt + 1)

    attemptFrom 1

/// Wraps `files` so every call is retried under `policy`.
let wrap (policy: RetryPolicy) (wait: TimeSpan -> unit) (files: RepositoryFiles) =
    { new RepositoryFiles with
        member _.Read path = run policy wait (fun () -> files.Read path)
        member _.CreateNew(path, content) = run policy wait (fun () -> files.CreateNew(path, content)) }
