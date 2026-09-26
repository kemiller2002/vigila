/// Tier 1 - who or what caused a change, and through which channel.
///
/// Actor answers "who did this"; CreatedVia answers "how did it arrive". They
/// are separate because an agent may act through the UI on a user's behalf, and
/// an import may carry a human's name. Neither may hold a secret
/// (VIG-DOM-037).
///
/// The actor is the Praxis provenance actor (VIG-DOM-050): the name is its
/// stable id, and a non-human actor also says which provider, model and
/// runtime it ran as. The mapping onto Praxis is ADR-0004.
///
/// Requirements: VIG-DOM-035, VIG-DOM-037, VIG-DOM-050, VIG-DOM-051,
/// VIG-PER-025.
module Vigila.Semantic.Actors

open Vigila.Semantic.Carried

/// What kind of thing made a change.
type ActorType =
    | Human
    | Agent
    | AutomatedProcess
    | Integration

/// What a non-human actor ran as (VIG-DOM-050).
///
/// Each value is the literal "unknown" when it is not known. It is never
/// guessed, and never absent: absence would mean "not applicable", which is
/// true only of a human, and a human has no Tooling at all.
type Tooling =
    { Provider: string
      Model: string
      Runtime: string }

[<RequireQualifiedAccess>]
module Tooling =

    [<Literal>]
    let UnknownValue = "unknown"

    let unknown =
        { Provider = UnknownValue
          Model = UnknownValue
          Runtime = UnknownValue }

/// Who made a change. The name is free text -- a person, an agent's name, a
/// process identifier -- because Vigila is not a contacts system
/// (VIG-DOM-024 makes the same point about WaitingOn). It is also the Praxis
/// actor's stable `id`.
type Actor =
    { Type: ActorType
      Name: string
      /// `None` exactly when the actor is a human (VIG-DOM-050).
      Tooling: Tooling option
      /// Fields a persisted actor carried that Vigila does not model, kept in
      /// order so a rewrite does not drop them (VIG-PER-025).
      Extensions: (string * Verbatim) list }

[<RequireQualifiedAccess>]
module Actor =

    let private trim (value: string | null) =
        match value with
        | Null -> ""
        | NonNull supplied -> supplied.Trim()

    /// An actor whose provider, model and runtime are not known. A non-human
    /// actor records them as "unknown" rather than leaving them out, because
    /// they apply to it (VIG-DOM-050).
    let create actorType (name: string | null) =
        let trimmed = trim name

        if trimmed.Length = 0 then
            Error "An actor name is required."
        else
            Ok
                { Type = actorType
                  Name = trimmed
                  Tooling = if actorType = Human then None else Some Tooling.unknown
                  Extensions = [] }

    let human name = create Human name

    let agent name = create Agent name

    /// A non-human actor that declared what it ran as.
    ///
    /// A blank value becomes "unknown": the declaration is taken at its word
    /// and nothing is filled in. A human is refused, because provider, model
    /// and runtime do not apply to one. A value shaped like a credential is
    /// refused, because identity never carries one.
    let identify actorType (name: string | null) (provider: string | null) (model: string | null) (runtime: string | null) =
        let orUnknown value =
            match trim value with
            | "" -> Tooling.UnknownValue
            | text -> text

        let tooling =
            { Provider = orUnknown provider
              Model = orUnknown model
              Runtime = orUnknown runtime }

        match create actorType name with
        | Error message -> Error message
        | Ok _ when actorType = Human -> Error "A human actor has no provider, model or runtime."
        | Ok actor when
            [ actor.Name; tooling.Provider; tooling.Model; tooling.Runtime ]
            |> List.exists Credentials.looksLikeCredential
            ->
            Error "An actor value looks like a credential; identity never carries one."
        | Ok actor -> Ok { actor with Tooling = Some tooling }

    /// Whether the actor keeps the human/non-human invariant, and carries no
    /// credential in the values Vigila models.
    let problems (actor: Actor) =
        [ if actor.Name.Trim().Length = 0 then
              "An actor name is required."
          match actor.Type, actor.Tooling with
          | Human, Some _ -> "A human actor has no provider, model or runtime."
          | Human, None -> ()
          | _, None -> "A non-human actor records provider, model and runtime, as \"unknown\" when not known."
          | _, Some tooling ->
              if [ tooling.Provider; tooling.Model; tooling.Runtime ] |> List.exists (fun v -> v.Trim().Length = 0) then
                  "Provider, model and runtime must not be empty; \"unknown\" says they are not known."
          if
              [ yield actor.Name
                match actor.Tooling with
                | Some tooling -> yield! [ tooling.Provider; tooling.Model; tooling.Runtime ]
                | None -> () ]
              |> List.exists Credentials.looksLikeCredential
          then
              "An actor value looks like a credential; identity never carries one." ]

/// The channel a change arrived through (VIG-DOM-037).
///
/// Qualified access is required because `Agent` and `Integration` also name
/// ActorType cases. The overlap is real -- an agent is both a kind of actor and
/// a channel -- and qualifying is preferable to inventing different words for
/// the same thing.
[<RequireQualifiedAccess>]
type CreatedVia =
    | UI
    | Agent
    | Integration
    | Import
