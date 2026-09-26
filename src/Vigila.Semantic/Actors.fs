/// Tier 1 - who or what caused a change, and through which channel.
///
/// Actor answers "who did this"; CreatedVia answers "how did it arrive". They
/// are separate because an agent may act through the UI on a user's behalf, and
/// an import may carry a human's name. Neither may hold a secret
/// (VIG-DOM-037).
///
/// When an item carries Praxis provenance (Vigila.Semantic.Provenance), that
/// block is the identity and an Actor here is only its display projection
/// (VIG-PROV-002, VIG-PROV-015).
///
/// Requirements: VIG-DOM-035, VIG-DOM-037, VIG-PROV-015.
module Vigila.Semantic.Actors

/// What kind of thing made a change.
///
/// `Unknown` exists only as the projection of a Praxis actor whose kind is
/// `unknown` or an `x-...` extension (VIG-PROV-015): mapping those onto
/// `Agent` or `AutomatedProcess` would fabricate a fact. It is never produced
/// from a schema-version-1 record, and a record that contains it is written as
/// schema version 2 so older readers refuse it rather than misread it
/// (VIG-PROV-007).
type ActorType =
    | Human
    | Agent
    | AutomatedProcess
    | Integration
    | Unknown

/// Who made a change. The name is free text -- a person, an agent's name, a
/// process identifier -- because Vigila is not a contacts system
/// (VIG-DOM-024 makes the same point about WaitingOn).
type Actor =
    { Type: ActorType
      Name: string }

[<RequireQualifiedAccess>]
module Actor =

    let create actorType (name: string | null) =
        let trimmed =
            match name with
            | Null -> ""
            | NonNull supplied -> supplied.Trim()

        if trimmed.Length = 0 then
            Error "An actor name is required."
        else
            Ok { Type = actorType; Name = trimmed }

    let human name = create Human name

    let agent name = create Agent name

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
