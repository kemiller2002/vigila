/// Tier 1 - recognizing a value that would leak a credential.
///
/// Identity and provenance name who acted and in which run. They never carry
/// authentication material (VIG-DOM-037, VIG-DOM-050). This recognizes the
/// unambiguous shapes of common credentials, so a value that would leak one is
/// refused rather than recorded. It guards against accidents and is not a
/// secret scanner: a value it does not recognize is not thereby proven safe.
///
/// The patterns are the ones the Praxis contract fixtures exercise
/// (RQ-ROS-2026-A013), kept local because Vigila takes no dependency on Praxis.
///
/// Requirements: VIG-DOM-050, VIG-AGT-037.
module Vigila.Semantic.Credentials

open System.Text.RegularExpressions

let private patterns =
    [ @"\bsk-(?:ant-|proj-)?[A-Za-z0-9_-]{16,}"
      @"\bgh[pousr]_[A-Za-z0-9]{20,}"
      @"\bgithub_pat_[A-Za-z0-9_]{20,}"
      @"\bxox[abposr]-[A-Za-z0-9-]{10,}"
      @"\bAKIA[0-9A-Z]{16}\b"
      @"\bAIza[0-9A-Za-z_-]{30,}"
      @"-----BEGIN [A-Z ]*PRIVATE KEY-----"
      @"(?i)\bbearer\s+[A-Za-z0-9._~+/=-]{16,}"
      @"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}"
      @"(?i)\b(?:api[_-]?key|access[_-]?token|secret|password|passwd)\s*[=:]\s*\S{8,}" ]
    |> List.map (fun pattern -> Regex(pattern, RegexOptions.CultureInvariant))

/// Whether a value has the shape of a credential.
let looksLikeCredential (value: string) =
    patterns |> List.exists (fun pattern -> pattern.IsMatch value)
