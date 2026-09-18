/// Tier 1 - time values.
///
/// Vigila distinguishes a whole day from a precise moment, and the distinction
/// is load-bearing: "due Tuesday" and "due Tuesday at 00:00 UTC" are different
/// facts, and conflating them shifts dates across time zones. The types here
/// keep them apart so that conflation is not expressible.
///
/// Requirements: VIG-TIME-015, VIG-TIME-016, VIG-TIME-017, VIG-TIME-019,
/// VIG-TIME-023.
module Vigila.Semantic.Time

open System

/// A precise moment, stored unambiguously (VIG-TIME-015).
///
/// Wraps DateTimeOffset rather than DateTime because a DateTime carries no
/// offset and therefore cannot represent a moment unambiguously.
type Instant =
    private
    | Instant of DateTimeOffset

    member this.Value = let (Instant v) = this in v

[<RequireQualifiedAccess>]
module Instant =

    let ofDateTimeOffset (value: DateTimeOffset) = Instant value

    let toDateTimeOffset (Instant value) = value

    /// Round-trip form, offset included.
    let toIso8601 (Instant value) =
        value.ToString("o", Globalization.CultureInfo.InvariantCulture)

    let parse (text: string) =
        match DateTimeOffset.TryParse(
                  text,
                  Globalization.CultureInfo.InvariantCulture,
                  Globalization.DateTimeStyles.RoundtripKind) with
        | true, value -> Ok(Instant value)
        | _ -> Error $"'%s{text}' is not a valid timestamp."

    let isBefore (Instant a) (Instant b) = a < b

    let isAfter (Instant a) (Instant b) = a > b

/// A due or follow-up value, which may be a whole day or a precise moment
/// (VIG-TIME-017).
///
/// `OnDate` deliberately carries no time and no offset. A date-only value that
/// were stored as a timestamp would land on the previous or next calendar day
/// for some readers, which VIG-TIME-016 forbids -- so the shift is prevented by
/// the type rather than by careful conversion at every boundary.
type WhenValue =
    | OnDate of DateOnly
    | AtInstant of Instant

[<RequireQualifiedAccess>]
module WhenValue =

    /// The calendar day a value falls on, for a reader in the given time zone.
    ///
    /// A date-only value is already a calendar day and is returned unchanged --
    /// the time zone cannot move it. Only a precise moment is projected, and
    /// doing so requires the caller to say which zone, because the core must
    /// not assume one (VIG-TIME-019).
    let dayIn (zone: TimeZoneInfo) value =
        match value with
        | OnDate day -> day
        | AtInstant instant ->
            let local = TimeZoneInfo.ConvertTime(Instant.toDateTimeOffset instant, zone)
            DateOnly.FromDateTime local.DateTime

/// A source of the current time (VIG-TIME-023).
///
/// Domain logic never reads ambient system time; it is handed a clock. The
/// system clock is an external effect and therefore belongs to Tier 4
/// (VIG-GOV-014) -- Tier 1 defines only the abstraction and a fixed clock for
/// tests.
[<NoEquality; NoComparison>]
type Clock =
    private
    | Clock of (unit -> Instant)

[<RequireQualifiedAccess>]
module Clock =

    let create (read: unit -> Instant) = Clock read

    /// A clock frozen at one moment, so a test can assert on exact timestamps.
    let fixedAt instant = Clock(fun () -> instant)

    let now (Clock read) = read ()
