// Limen kernel side - browser interop only.
//
// Implements Limen's EngineTransport by loading the published .NET WASM runtime
// and forwarding each message, as JSON, to the Vigila.Wasm shim's single
// [JSExport] method. It never inspects a message's contents: this file is
// mechanics, and every application decision belongs to the F# engine behind it.
//
// It does inspect one thing: outbound Http effects, so the kernel-side token
// can be attached to them. The engine never supplies a credential and never
// sees one (VIG-SEC-005), so this is the seam where authority's request becomes
// a capability's authorized request. It is still mechanics — the decision to
// call GitHub was the engine's; only the credential is added here.
//
// Requirements: VIG-GOV-008, VIG-GOV-011, VIG-GOV-015, VIG-SEC-005.

// Relative, not rooted at the domain: a dynamic import() with a relative
// specifier resolves against this module's own URL, so the app keeps working
// whether it is served from a domain root or a subpath, as long as web/ and
// src/ stay siblings under whatever the site root is.
const FRAMEWORK_BASE = "../src/Vigila.Wasm/bin/Release/net10.0/publish/wwwroot/_framework";

export class WasmEngineTransport {
  #exports = null;
  #tokens;

  constructor(tokens) {
    this.#tokens = tokens;
  }

  async start() {
    const { dotnet } = await import(`${FRAMEWORK_BASE}/dotnet.js`);
    const { getAssemblyExports, getConfig } = await dotnet.withDiagnosticTracing(false).create();
    const exports = await getAssemblyExports(getConfig().mainAssemblyName);

    if (!exports.VigilaWasm || typeof exports.VigilaWasm.Dispatch !== "function") {
      throw new Error(
        "VigilaWasm.Dispatch export not found — run `npm run build:wasm` to republish the engine.",
      );
    }

    this.#exports = exports;
  }

  async dispatch(message) {
    if (!this.#exports) {
      throw new Error("WasmEngineTransport.dispatch() called before start()");
    }

    const response = JSON.parse(this.#exports.VigilaWasm.Dispatch(JSON.stringify(message)));

    return { ...response, effects: response.effects.map((effect) => this.#authorize(effect)) };
  }

  // Adds the credential to a GitHub request on its way to the kernel. Any other
  // effect, and any other origin, passes through untouched.
  #authorize(effect) {
    if (effect.kind !== "Http") return effect;

    const headers = this.#tokens.authorize(effect.url, effect.headers ?? {});
    return headers === effect.headers ? effect : { ...effect, headers };
  }
}
