// An operational fault, end to end, on the single WebAssembly thread.
//
// The engine runs on the browser's one WASM thread. Aegis 2.0.1's detached
// capture waited synchronously for its fault record there, so the first
// operational fault froze the page; 2.0.2 records it without waiting. This
// test proves both halves of the boundary contract in the browser: the fault
// is presented (safely, with an AG- reference), and the page keeps answering.
//
// The engine reads every well-formed message defensively, and the transport
// only ever sends JSON it serialized itself, so no ordinary interaction faults.
// The one fault-injection seam is therefore the transport: the page is served
// a copy of it that truncates exactly one message, the one carrying the
// sentinel title, into malformed JSON. Everything else stays real: the DOM
// event, Limen's kernel, the [JSExport] shim, the F# engine, Aegis, and the
// rendered fault.
import { expect, test } from "@playwright/test";
import { openConnected } from "./support.js";

const SENTINEL = "malformed-message-sentinel";
const DISPATCH = "this.#exports.VigilaWasm.Dispatch(JSON.stringify(message))";

/// Serves a transport whose dispatch truncates the sentinel's message.
const injectMalformedMessage = async (page) => {
  await page.route("**/web/wasm-engine-transport.js", async (route) => {
    const original = await (await route.fetch()).text();
    // Fails loudly if the transport changes shape, rather than silently
    // injecting nothing and passing.
    expect(original).toContain(DISPATCH);

    const corrupt = `((json) => (json.includes(${JSON.stringify(SENTINEL)}) ? json.slice(0, -1) : json))(JSON.stringify(message))`;
    await route.fulfill({
      contentType: "text/javascript",
      body: original.replace(DISPATCH, `this.#exports.VigilaWasm.Dispatch(${corrupt})`),
    });
  });
};

test("an operational fault is shown, and the page keeps responding", async ({ page }) => {
  await injectMalformedMessage(page);
  const { failures } = await openConnected(page);

  await page.fill("#title", SENTINEL);

  const fault = page.locator("#operational-fault");
  await expect(fault).toBeVisible();
  await expect(fault).toHaveAttribute("role", "alert");
  await expect(page.locator("#operational-fault-title")).toHaveText("Vigila could not complete that operation");
  await expect(fault.locator(".ef-fault__message")).toHaveText(
    "Vigila could not process an application message. The rest of the application is still available.",
  );
  await expect(fault.locator("code")).toHaveText(/^AG-/);
  // The safe presentation only: never the exception.
  await expect(fault).not.toContainText("JsonException");

  // Responsive: the next ordinary message is decided, and it clears the fault.
  await page.fill("#title", "Call the accountant");
  await expect(fault).toHaveCount(0);
  await expect(page.locator(".capture__submit")).toBeEnabled();
  await page.click(".capture__submit");
  await expect(page.locator(".item__title")).toHaveText(["Call the accountant"]);

  // The Aegis fault record reaches the console through standard error; that
  // is the one console error this test provokes, and nothing else may appear.
  expect(failures().filter((failure) => !/VIGILA\.BOUNDARY\.MESSAGE_INVALID/.test(failure))).toEqual([]);
});
