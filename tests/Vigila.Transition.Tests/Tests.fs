module Vigila.Transition.Tests

open Xunit
open Vigila.Semantic.Items
open Vigila.Transition.Transitions

/// Every state, so the tests below enumerate rather than sample.
let private allStates = [ Open; Waiting; Deferred; Completed; Cancelled ]

/// The legal transitions exactly as VIG-DOM-009 tabulates them, written out
/// independently of the implementation so that a change to the production
/// table cannot silently change what the tests assert.
let private specifiedLegal =
    [ Open, Waiting
      Open, Deferred
      Open, Completed
      Open, Cancelled

      Waiting, Open
      Waiting, Deferred
      Waiting, Completed
      Waiting, Cancelled

      Deferred, Open
      Deferred, Waiting
      Deferred, Completed
      Deferred, Cancelled

      Completed, Open

      Cancelled, Open ]

// VIG-TST-001 requires every legal transition AND every illegal transition to
// be covered. Enumerating the full cross-product does both exhaustively.

[<Fact>]
let ``every transition the requirements permit is accepted`` () =
    for from, to' in specifiedLegal do
        match transition from to' with
        | Ok result -> Assert.Equal(to', result)
        | Error refusal -> failwith $"%A{from} -> %A{to'} should be legal but was refused: %s{refusal.Describe}"

[<Fact>]
let ``every transition the requirements omit is refused`` () =
    // VIG-DOM-012: illegal transitions are prevented by the domain.
    let permitted = Set.ofList specifiedLegal

    for from in allStates do
        for to' in allStates do
            if from <> to' && not (permitted.Contains((from, to'))) then
                match transition from to' with
                | Error(NotLegal(a, b)) ->
                    Assert.Equal(from, a)
                    Assert.Equal(to', b)
                | Error other -> failwith $"%A{from} -> %A{to'} refused for the wrong reason: %A{other}"
                | Ok _ -> failwith $"%A{from} -> %A{to'} should be illegal but was accepted."

[<Fact>]
let ``a terminal state cannot move directly to the other terminal state`` () =
    // Named explicitly because it is the pair most likely to be "fixed" by
    // mistake: closing a cancelled item, or cancelling a completed one, must
    // go through Open (VIG-DOM-042).
    Assert.True(Result.isError (transition Completed Cancelled))
    Assert.True(Result.isError (transition Cancelled Completed))

[<Fact>]
let ``both terminal states can be reopened`` () =
    // VIG-DOM-042.
    Assert.Equal(Ok Open, transition Completed Open)
    Assert.Equal(Ok Open, transition Cancelled Open)

[<Fact>]
let ``a transition to the current state is refused as redundant`` () =
    for state in allStates do
        match transition state state with
        | Error(AlreadyInState s) -> Assert.Equal(state, s)
        | other -> failwith $"%A{state} -> itself should report AlreadyInState, got %A{other}"

[<Fact>]
let ``isLegal agrees with transition`` () =
    // The two are separate entry points; they must not drift apart.
    for from in allStates do
        for to' in allStates do
            Assert.Equal(isLegal from to', Result.isOk (transition from to'))

[<Fact>]
let ``availableFrom offers exactly the legal targets`` () =
    for from in allStates do
        let expected = specifiedLegal |> List.filter (fst >> (=) from) |> List.map snd |> Set.ofList

        Assert.Equal<Set<ItemStatus>>(expected, availableFrom from |> Set.ofList)

[<Fact>]
let ``every state is reachable and every state can be left`` () =
    // Guards against a state becoming a dead end, which would strand items.
    for state in allStates do
        Assert.NotEmpty(availableFrom state)
        Assert.True(allStates |> List.exists (fun other -> isLegal other state))
