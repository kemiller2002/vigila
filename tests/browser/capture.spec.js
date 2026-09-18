// The composed browser path, observed end to end.
//
// Every assertion here crosses the whole boundary: DOM event -> Limen kernel ->
// JSON -> [JSExport] shim -> F# engine -> JSON -> kernel -> DOM. A test that
// reached into the engine directly would prove nothing this suite exists for,
// so nothing below touches anything but the page.
//
// Requirements: VIG-UI-010, VIG-UI-011, VIG-UI-012, VIG-UI-020, VIG-GOV-013.
import { expect, test } from "@playwright/test";

const PAGE = "/web/index.html";

const selectors = {
  input: "#title",
  submit: ".capture__submit",
  empty: ".empty",
  error: "#capture-error",
  items: ".item",
  title: ".item__title",
  kind: ".item__kind",
  status: ".item__status",
  id: ".item__id",
};

/// Opens the page and fails the test on any console error or uncaught
/// exception. A silently dead engine renders an empty page and passes a naive
/// assertion; it does not pass this.
const openPage = async (page) => {
  const failures = [];

  page.on("console", (message) => {
    if (message.type() === "error") failures.push(`console: ${message.text()}`);
  });
  page.on("pageerror", (error) => failures.push(`pageerror: ${error.message}`));

  await page.goto(PAGE);
  // The kernel renders the first view only after the WASM runtime has started,
  // so the empty state appearing is the signal that the whole path is live.
  await expect(page.locator(selectors.empty)).toBeVisible();

  return () => failures;
};

const capture = async (page, title) => {
  await page.fill(selectors.input, title);
  await page.click(selectors.submit);
};

test("the engine reaches the browser and renders its first view", async ({ page }) => {
  const failures = await openPage(page);

  await expect(page.locator(selectors.empty)).toHaveText("Nothing open yet.");
  await expect(page.locator(selectors.items)).toHaveCount(0);
  expect(failures()).toEqual([]);
});

test("capture is refused until the engine says a title is legal", async ({ page }) => {
  const failures = await openPage(page);

  // The disabled state is the engine's decision (VIG-GOV-013) arriving through
  // data-bind-disabled, not something the HTML worked out for itself.
  await expect(page.locator(selectors.submit)).toBeDisabled();

  await page.fill(selectors.input, "Call the accountant");
  await expect(page.locator(selectors.submit)).toBeEnabled();

  // Whitespace only is not a title, and the engine is the one that knows it.
  await page.fill(selectors.input, "   ");
  await expect(page.locator(selectors.submit)).toBeDisabled();

  expect(failures()).toEqual([]);
});

test("a captured item is rendered with the fields the engine projects", async ({ page }) => {
  const failures = await openPage(page);

  await capture(page, "Call the accountant about the generator");

  await expect(page.locator(selectors.items)).toHaveCount(1);
  await expect(page.locator(selectors.title)).toHaveText(
    "Call the accountant about the generator",
  );
  await expect(page.locator(selectors.kind)).toHaveText("Task");
  await expect(page.locator(selectors.status)).toHaveText("Open");
  // The identifier is the engine's, so its shape is not asserted here — only
  // that one crossed the boundary at all.
  await expect(page.locator(selectors.id)).not.toBeEmpty();

  await expect(page.locator(selectors.empty)).toHaveCount(0);
  expect(failures()).toEqual([]);
});

test("capture clears the draft and accepts another item immediately", async ({ page }) => {
  const failures = await openPage(page);

  await capture(page, "First");
  // VIG-UI-012: the form clears only on success, and the next capture starts
  // from empty rather than duplicating the previous title.
  await expect(page.locator(selectors.input)).toHaveValue("");
  await expect(page.locator(selectors.submit)).toBeDisabled();

  await capture(page, "Second");

  await expect(page.locator(selectors.items)).toHaveCount(2);
  // Newest first, which is the engine's ordering reaching the DOM intact.
  await expect(page.locator(selectors.title)).toHaveText(["Second", "First"]);
  expect(failures()).toEqual([]);
});

test("an illegal draft cannot be submitted, and the text survives the attempt", async ({ page }) => {
  const failures = await openPage(page);

  await page.fill(selectors.input, "   ");
  await page.press(selectors.input, "Enter");

  // The browser refuses implicit submission while the form's default button is
  // disabled, and the engine disables it for exactly the drafts it would
  // refuse. So the refusal never reaches the engine by this route: no item is
  // created, and no message is shown either.
  await expect(page.locator(selectors.items)).toHaveCount(0);
  await expect(page.locator(selectors.error)).toHaveCount(0);

  // VIG-UI-013: whatever happens, the typed text is not discarded.
  await expect(page.locator(selectors.input)).toHaveValue("   ");

  // Correcting the draft makes capture legal again.
  await page.fill(selectors.input, "Something real");
  await expect(page.locator(selectors.submit)).toBeEnabled();

  expect(failures()).toEqual([]);
});

// The engine projects `error` and `hasError`, and index.html binds them, but
// nothing in the current UI can reach that state: the only path to `Capture`
// is the submit button, and it is disabled for precisely the drafts that would
// be refused. The binding is therefore live but unreachable — a present branch
// with no observable behaviour, which VERIFICATION-METHOD.md calls a semantic
// no-op and treats as a first-class defect.
//
// It is left failing-free rather than asserted, because reaching it is a design
// decision (announce on submit, hint on blur, or enable-and-refuse) and not
// this work item's to make. Filed as WI-0021.
test.fixme("a refused capture shows the engine's reason", async ({ page }) => {
  const failures = await openPage(page);

  await page.fill(selectors.input, "   ");
  await page.press(selectors.input, "Enter");

  const error = page.locator(selectors.error);
  await expect(error).toBeVisible();
  await expect(error).toHaveAttribute("role", "alert");
  await expect(error).not.toBeEmpty();
  expect(failures()).toEqual([]);
});

test("the capture control is reachable and operable by keyboard alone", async ({ page }) => {
  const failures = await openPage(page);

  // VIG-UI-021: core operations must not require a mouse.
  await page.keyboard.press("Tab");
  await expect(page.locator(selectors.input)).toBeFocused();

  await page.keyboard.type("Typed without a mouse");
  await page.keyboard.press("Enter");

  await expect(page.locator(selectors.items)).toHaveCount(1);
  await expect(page.locator(selectors.title)).toHaveText("Typed without a mouse");
  expect(failures()).toEqual([]);
});
