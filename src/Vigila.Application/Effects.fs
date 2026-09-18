/// Tier 3 - the effects the engine may ask the browser kernel to perform.
///
/// Limen splits capability from authority: the engine decides *what* should
/// happen and the kernel is the only thing that can actually touch the network
/// or storage. This module is the vocabulary of that request.
///
/// One constructor per legal operation, rather than one record with optional
/// fields. Keeping the algebra closed across the Host Contract is the rule in
/// `.sde/architecture/BOUNDARY-PRESERVATION.md`, and it is what makes the
/// renderer in `Dispatch` exhaustive: a new effect cannot be added without the
/// compiler demanding its wire form.
///
/// Note what an Http effect does not carry: credentials. The token lives
/// kernel-side and the transport attaches it. See `Connection` for why.
///
/// Requirements: VIG-GOV-014, VIG-GOV-015, VIG-SEC-005.
module Vigila.Application.Effects

type HttpMethod =
    | Get
    | Put
    | Post
    | Patch
    | Delete

type StorageOperation =
    | StorageGet of key: string
    | StorageSet of key: string * value: string
    | StorageRemove of key: string

type NavigationOperation =
    | NavigatePush of url: string
    | NavigateReplace of url: string
    | NavigateBack
    | NavigateForward

type HttpEffect =
    { CorrelationId: string
      Method: HttpMethod
      Url: string
      Headers: (string * string) list
      Body: string option
      TimeoutMs: int }

/// The four capabilities Limen's protocol declares. Vigila requests none of
/// them yet; the set is complete so that it mirrors the protocol rather than
/// the subset one feature happened to need.
type Effect =
    | Http of HttpEffect
    | Storage of correlationId: string * operation: StorageOperation
    | Clipboard of correlationId: string * text: string
    | Navigation of correlationId: string * operation: NavigationOperation

