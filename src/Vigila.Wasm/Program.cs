using System.Runtime.InteropServices.JavaScript;

// Entry point required by the WebAssembly SDK. The module is driven entirely
// through the [JSExport] below; nothing runs here.
return;

// Limen kernel side / SDE Tier 4 (Host - External Effects): owns WASM and
// browser interop (.sde/architecture/FOUR-TIER-ARCHITECTURE.md).
//
// A pure marshalling shim. It forwards a JSON string to
// Vigila.Application.Dispatch.handle and returns whatever that returns. It must
// never contain a business decision: every application rule lives in F#, in
// Vigila.Semantic, Vigila.Transition and Vigila.Application.
public partial class VigilaWasm
{
    [JSExport]
    internal static string Dispatch(string messageJson) =>
        Vigila.Application.Dispatch.handle(messageJson);
}
