---
id: ADR-0003
title: The F# engine reaches the browser through Limen as WebAssembly
status: accepted
created: 2026-09-18
work_item: WI-0014
---

# ADR-0003 — Limen boundary and the WASM engine

## Context

`VIG-GOV-008` requires Limen for the browser/application boundary, and
`VIG-GOV-009` requires F# wherever practical. Limen's own engine is
**TypeScript**: `DirectTypeScriptTransport` is what ships, and its
`docs/17-wasm-migration.md` says plainly that the repository contains no
WebAssembly and that the claim an app can be moved behind `EngineTransport`
into WASM is "currently untested, because no one has done it."

Vigila has no server. `VIG-SEC-001` puts the user's token in the browser and
talks to GitHub directly, so F# must run in the browser or not run at all.

## Decision

Follow the pattern already proven in the sibling `time-tracking-application`:
the F# engine is published to WebAssembly and driven through Limen's
`EngineTransport`.

```
web/index.html            structure and data-* bindings only
web/main.js               BrowserKernel(new WasmEngineTransport(), document)
web/wasm-engine-transport.js   implements EngineTransport; loads dotnet.js
        │ JSON string
src/Vigila.Wasm           C# [JSExport] shim, Microsoft.NET.Sdk.WebAssembly
        │ Dispatch.handle
src/Vigila.Application    the engine: state, commands, transitions, projection
src/Vigila.Transition     legal changes
src/Vigila.Semantic       what can be true
```

### Limen's boundary maps onto the four tiers

`limen.config.json` is user-owned and takes any number of paths, so it names
Vigila's actual layout rather than Limen's default `src/engine` and
`src/kernel`:

| Limen | Contains | SDE tier |
|---|---|---|
| `engine` | `Vigila.Semantic`, `Vigila.Transition`, `Vigila.Application`, `Vigila.Host.GitHub` | 1–3, and the pure parts of 4 |
| `kernel` | `Vigila.Wasm`, `web` | 4 — browser and WASM interop |

`Vigila.Host.GitHub` sits on the **engine** side despite its name. Limen's split
is capability versus authority, and that project touches no browser API: it
computes paths and serialises records. The fetch it describes is performed by
the kernel.

### The shim makes no decisions

`src/Vigila.Wasm/Program.cs` is one `[JSExport]` method forwarding a JSON
string. That is deliberate and matches the sibling's rule verbatim: every
application rule stays in F#, so there is one semantic authority rather than a
TypeScript engine that could disagree with the domain.

The engine defers to the domain for the one rule capture needs — it calls
`Title.create` rather than re-checking "a title is required", so the two cannot
drift.

### Rejected: Fable

Compiling F# to JavaScript with Fable would also put the domain in the browser,
but it is not the family's pattern, it would not compile `ItemJson`
(`System.Text.Json` is unsupported), and it would force a second serialiser and
therefore a second wire-format authority.

### Rejected: a TypeScript engine

The shortest path, and the one Limen's examples show. Rejected because the
domain rules would then exist twice — once in F#, once in TypeScript — which is
precisely the single-semantic-authority failure SDE names.

## Consequences

- **Reading the clock happens at the composition root.** `VIG-TIME-023` keeps
  ambient time out of domain logic, and Limen's protocol has no Time effect, so
  `Dispatch` builds the `Clock` and passes it in. If a Time effect is added,
  that is the one place to change.
- **Identifiers must avoid browser globals.** Limen's boundary check matches
  names textually, so a local named `document` fails it even in F#. Two were
  renamed. This is a real constraint on engine code, not a one-off.
- **The published engine is ~13 MB unoptimised.** The `wasm-tools` workload
  would trim it; that is a deployment concern, not an architectural one.
- **Bindings are checked against the projection.** Limen fills the HTML by key
  name, so a renamed key fails silently in the browser.
  `scripts/check-view-bindings.sh` turns that into a build failure, and was
  verified to catch both a misspelled binding and an unknown event.

## What this slice does and does not do

It captures an item and lists it, with the real domain rule enforced in F#
across the WASM boundary. **Nothing is persisted yet** — `effects` is always
empty. The connection flow (`VIG-SEC-001` … `VIG-SEC-012`) and the GitHub
effects are the next slice, and the protocol already carries them: the engine
will return an `Http` effect request and the kernel will perform it.
