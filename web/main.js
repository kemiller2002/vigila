// Limen kernel side - browser interop only.
//
// Three lines of meaning: build the transport, hand it to Limen's kernel, start.
// Everything the application decides happens on the far side of that transport,
// in F#.
import { BrowserKernel } from "../node_modules/@echelon-foundry/typescript-wasm-kernel/dist/kernel/browser-kernel.js";
import { WasmEngineTransport } from "./wasm-engine-transport.js";

await new BrowserKernel(new WasmEngineTransport(), document).start();
