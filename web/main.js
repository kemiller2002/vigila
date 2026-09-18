// Limen kernel side - browser interop only.
//
// Builds the transport, gives it the token store, hands both to Limen's kernel
// and starts. Everything the application decides happens on the far side of
// that transport, in F#.
//
// The diagnostics sink is not optional decoration. Limen reports a bridge
// failure to it rather than throwing, so the default no-op sink makes a broken
// engine look like an empty page — which is exactly how a reflection failure in
// the engine went unnoticed until a browser test caught it. See
// docs/architecture/SDE-CONFORMANCE-2026-09-18.md, F-5.
import { BrowserKernel } from "../node_modules/@echelon-foundry/typescript-wasm-kernel/dist/kernel/browser-kernel.js";
import { WasmEngineTransport } from "./wasm-engine-transport.js";
import { TokenStore, bindCredential } from "./connection.js";

const tokens = new TokenStore();

// Limen reports exactly two kinds of event. Only one of them is a failure, and
// treating the other as one would train everybody to ignore the console — which
// is the same way a real bridge error goes unnoticed.
const diagnostics = {
  report(event) {
    if (event.kind === "BridgeError") {
      // A bridge error means the engine is not answering. A silent page is the
      // worst possible way to say so, so this is loud.
      console.error(`[limen] bridge error during ${event.phase}: ${event.detail}`);
      return;
    }

    console.debug("[limen]", event);
  },
};

await new BrowserKernel(new WasmEngineTransport(tokens), document, diagnostics).start();

// Bound after start(): the setup form only exists once the engine has rendered
// its first view, because it lives inside a data-if template.
bindCredential(document, tokens);
