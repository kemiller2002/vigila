/// Tier 1 - Semantic Model. What can be true of an item.
///
/// This module says nothing about how an item changes; that is Tier 2
/// (Vigila.Transition). It says only which values are representable.
///
/// Requirements: VIG-DOM-005, VIG-DOM-008, VIG-DOM-014, VIG-DOM-048,
/// VIG-GOV-012.
module Vigila.Semantic.Items

/// The kinds an item may take.
///
/// Three kinds, not four. v0.1 and v0.2 of the requirements also specified a
/// `Reminder` kind; v0.3 section 85 withdrew it on the grounds that a reminder
/// is timing attached to an item rather than a kind of item, and reminder
/// behaviour is expressed with a follow-up date or a snooze instead
/// (VIG-DOM-006).
///
/// This conflicts with the v1 scope list in v0.2 section 54, which still names
/// Reminder. That conflict is open as OQ-01; the later document governs until
/// it is settled. Adding a case here is the whole change if it is reversed.
///
/// Requirement: VIG-DOM-005.
type ItemKind =
    /// Something the user needs to do.
    | Task
    /// Something the user needs to revisit.
    | FollowUp
    /// Something dependent on another person, organisation, process or event.
    | Waiting

/// The workflow states an item may occupy (VIG-DOM-008).
///
/// `Waiting` appears both here and in ItemKind. The two are independent: a
/// Task may be in state Waiting. See OQ-02 - the requirements never state the
/// relationship, and treating them as independent is the only reading
/// consistent with WaitingSince and the Waiting view.
///
/// Snooze is deliberately absent. v0.3 section 56 makes snooze presentation
/// timing rather than a workflow state, so it is not representable here
/// (VIG-TIME-011).
type ItemStatus =
    | Open
    | Waiting
    | Deferred
    | Completed
    | Cancelled

/// An item's title.
///
/// Private constructor: a Title value cannot exist unless it passed
/// validation, so "empty title" is unrepresentable rather than merely
/// rejected at the edge (VIG-GOV-012, VIG-DOM-048).
[<Struct>]
type Title =
    private
    | Title of string

    member this.Value = let (Title v) = this in v

    override this.ToString() = this.Value

[<RequireQualifiedAccess>]
module Title =

    /// Documented practical limit (VIG-DOM-047). Generous for normal use while
    /// preventing pathological payloads.
    [<Literal>]
    let MaxLength = 500

    /// Creates a title, rejecting empty or whitespace-only input
    /// (VIG-DOM-048) and input beyond the documented limit (VIG-DOM-047).
    /// Surrounding whitespace is trimmed; interior text is preserved exactly,
    /// including non-ASCII characters (VIG-DOM-047 requires Unicode safety).
    let create (text: string | null) =
        match text with
        | Null -> Error "A title is required."
        | NonNull supplied ->
            let trimmed = supplied.Trim()

            if trimmed.Length = 0 then
                Error "A title is required."
            elif trimmed.Length > MaxLength then
                Error $"A title may be at most %d{MaxLength} characters; got %d{trimmed.Length}."
            else
                Ok(Title trimmed)

    let value (title: Title) = title.Value
