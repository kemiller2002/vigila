// UI-LAYOUT-INTERACTION@1.0.0 conformance for Vigila's page (web/index.html),
// vigila#36 / GH-36. Each test cites the rules it observes; actions and peers
// are declared in vigila.contract.mjs by accessible role and name. Reports are
// attached to each test and become the evidence (tools/ui-conformance/
// evidence.mjs). GitHub is stubbed at the network boundary, as in
// tests/browser, so everything from the DOM to `fetch` is real.
import { test, expect, STATUS, record, setWidth, expectOutcome, recordObserved, expectRowOrStack, expectNoOverflowRecorded, scaleDefaultText } from "./support.mjs";
import { inventoryActions, recordTabSequence, wasFocusedDuringSequence, settleFontsAndFrames } from "./forma-conformance/index.mjs";
import { CONTRACT_WIDTHS, declaredActionFor } from "./vigila.contract.mjs";
import { PAGE, TOKEN, REPOSITORY, stubGitHub } from "../browser/support.js";

// A title with no break opportunity: the long-label case (ULI-L05).
const LONG = "Renegotiate-the-generator-maintenance-contract-with-the-facilities-provider";

const watchErrors = (page) => {
  const problems = [];
  page.on("console", (message) => { if (message.type() === "error") problems.push(message.text()); });
  page.on("pageerror", (error) => problems.push(error.message));
  return problems;
};

async function openSetup(page) {
  await page.goto(PAGE);
  await expect(page.getByRole("heading", { name: "Connect to a repository" })).toBeVisible();
}

async function fillSetup(page, token = TOKEN) {
  await page.getByLabel("GitHub token").fill(token);
  await page.getByRole("textbox", { name: "Repository" }).fill(REPOSITORY);
}

async function openConnected(page, titles = []) {
  await openSetup(page);
  await stubGitHub(page);
  await fillSetup(page);
  await page.getByRole("button", { name: "Connect" }).click();
  await expect(page.getByRole("textbox", { name: "What is open?" })).toBeVisible();
  for (const title of titles) {
    await page.getByRole("textbox", { name: "What is open?" }).fill(title);
    await page.getByRole("button", { name: "Capture" }).click();
    await expect(page.getByRole("region", { name: "Needs your attention" }).getByText(title, { exact: true })).toBeVisible();
  }
}

const undeclared = async (page) =>
  (await inventoryActions(page)).filter((control) => !declaredActionFor(control)).map((control) => `${control.role} "${control.name}"`);

// ---- ULI-A10: every enabled action is declared -------------------------------

test("every enabled action, in setup and connected with items, is declared (ULI-A01, ULI-A09, ULI-A10)", async ({ page }) => {
  const missing = [];
  for (const width of [1280, 390]) {
    await setWidth(page, width);
    await openSetup(page);
    await stubGitHub(page);
    await fillSetup(page);
    missing.push(...(await undeclared(page)).map((item) => `setup@${width}: ${item}`));
    await page.getByRole("button", { name: "Connect" }).click();
    await expect(page.getByRole("textbox", { name: "What is open?" })).toBeVisible();
    await page.getByRole("textbox", { name: "What is open?" }).fill("Call the accountant");
    missing.push(...(await undeclared(page)).map((item) => `workspace@${width}: ${item}`));
    await page.getByRole("button", { name: "Capture" }).click();
    await expect(page.getByRole("region", { name: "Needs your attention" }).getByText("Call the accountant", { exact: true })).toBeVisible();
    missing.push(...(await undeclared(page)).map((item) => `workspace with items@${width}: ${item}`));
    await page.unrouteAll({ behavior: "ignoreErrors" });
    await page.evaluate(() => window.localStorage.clear());
  }
  await record({ kind: "inventory", id: "inventory" }, {
    ruleId: "ULI-A10",
    status: missing.length === 0 ? STATUS.PASS : STATUS.FAIL,
    reason: missing.length === 0 ? "every enabled, visible action is declared in vigila.contract.mjs" : `undeclared: ${missing.join("; ")}`
  });
  expect(missing).toEqual([]);
});

// ---- Actions and their outcomes ---------------------------------------------------

test("Connect is refused with the reason for a rejected token, then connects once the token is right (ULI-A02, ULI-A04, ULI-A07)", async ({ page }) => {
  const problems = watchErrors(page);
  await openSetup(page);
  const connect = page.getByRole("button", { name: "Connect" });
  // ULI-A04: disabled until a token is entered, and the button says why.
  await expect(connect).toBeDisabled();
  await expect(connect).toHaveAccessibleDescription(/Required to connect\./);

  await recordObserved("setup.connect", "ULI-A07", async () => {
    // A rejected token: an alert names the problem and the application stays closed.
    await stubGitHub(page, { repoStatus: 401 });
    await fillSetup(page, "ghp_rejectedtoken0123456789");
    await connect.click();
    await expect(page.getByRole("alert")).toContainText("Connection needs attention");
    await expect(page.getByRole("textbox", { name: "What is open?" })).toHaveCount(0);
    // Recovery: the right token connects.
    await page.unrouteAll({ behavior: "ignoreErrors" });
    await stubGitHub(page);
    await fillSetup(page);
    await connect.click();
    await expect(page.getByRole("textbox", { name: "What is open?" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Connect to a repository" })).toHaveCount(0);
  });
  // The refused attempt is an ordinary outcome, not a page error.
  expect(problems.filter((text) => !text.startsWith("Failed to load resource"))).toEqual([]);
});

test("Connect opens the application (ULI-A02)", async ({ page }) => {
  await openSetup(page);
  await stubGitHub(page);
  await fillSetup(page);
  await expectOutcome(page, "setup.connect", { observe: { kind: "visible", target: { role: "textbox", name: "What is open?" }, expected: true } });
});

test("Capture lists the item and clears the draft; Disconnect returns to setup and forgets the token (ULI-A02, ULI-A05)", async ({ page }) => {
  await openConnected(page);
  await page.getByRole("textbox", { name: "What is open?" }).fill("Call the accountant");
  await expectOutcome(page, "workspace.capture", { observe: { kind: "visible", target: { role: "region", name: "Needs your attention" }, expected: true } });
  await expect(page.getByRole("region", { name: "Needs your attention" }).getByText("Call the accountant", { exact: true })).toBeVisible();
  await expect(page.getByRole("textbox", { name: "What is open?" })).toHaveValue("");
  await expectOutcome(page, "workspace.disconnect", { observe: { kind: "visible", target: { role: "heading", name: "Connect to a repository" }, expected: true } });
  expect(await page.evaluate(() => window.localStorage.getItem("vigila.token"))).toBeNull();
});

test("the keyboard reaches every setup field and Connect (ULI-A08)", async ({ page }) => {
  await openSetup(page);
  await page.getByLabel("GitHub token").fill(TOKEN);
  await page.locator("body").focus();
  await recordTabSequence(page, 6);
  const targets = [
    ["GitHub token", page.getByLabel("GitHub token")],
    ["Repository", page.getByRole("textbox", { name: "Repository" })],
    ["Branch", page.getByRole("textbox", { name: "Branch" })],
    ["Connect", page.getByRole("button", { name: "Connect" })]
  ];
  const missed = [];
  for (const [label, locator] of targets) if (!(await wasFocusedDuringSequence(locator))) missed.push(label);
  await record({ kind: "surface", id: "setup", check: "tab sequence" }, { ruleId: "ULI-A08", status: missed.length ? STATUS.FAIL : STATUS.PASS, reason: missed.length ? `not reached: ${missed.join(", ")}` : "each reached by Tab" });
  expect(missed).toEqual([]);
});

// ---- Layout -------------------------------------------------------------------------

test("submit buttons keep their natural size on the form's start edge, full width only on narrow screens (ULI-L03, ULI-L10)", async ({ page }) => {
  for (const width of CONTRACT_WIDTHS) {
    await setWidth(page, width);
    await openSetup(page);
    await page.getByLabel("GitHub token").fill(TOKEN);
    await settleFontsAndFrames(page);
    const [button, field] = await Promise.all([
      page.getByRole("button", { name: "Connect" }).boundingBox(),
      page.getByRole("textbox", { name: "Repository" }).boundingBox()
    ]);
    const narrow = width <= 480;
    const ok = Math.abs(button.x - field.x) <= 1 && (narrow ? Math.abs(button.width - field.width) <= 1 : button.width < field.width - 1);
    await record({ kind: "surface", id: "setup", check: "submit on the start edge", width, layout: narrow ? "full" : "start" }, {
      ruleId: "ULI-L10", status: ok ? STATUS.PASS : STATUS.FAIL,
      reason: `Connect x=${button.x.toFixed(1)} w=${button.width.toFixed(1)}; field x=${field.x.toFixed(1)} w=${field.width.toFixed(1)}`
    });
    expect(ok, `Connect @${width}: ${button.x},${button.width} vs field ${field.x},${field.width}`).toBe(true);
  }
});

test("setup: no overflow or clipping at 320/390/768/1280, at 100% and 200% text (ULI-L05, ULI-L09)", async ({ browser, baseURL }) => {
  for (const scale of [1, 2]) {
    for (const width of CONTRACT_WIDTHS) {
      const context = await browser.newContext({ baseURL, viewport: { width, height: 900 } });
      const page = await context.newPage();
      try {
        await openSetup(page);
        if (scale === 2) await scaleDefaultText(page, 2);
        await expectNoOverflowRecorded(page, "setup", width, scale);
        // The refusal state keeps its geometry too (ULI-L08).
        await stubGitHub(page, { repoStatus: 401 });
        await fillSetup(page, "ghp_rejectedtoken0123456789");
        await page.getByRole("button", { name: "Connect" }).click();
        await expect(page.getByRole("alert")).toBeVisible();
        await expectNoOverflowRecorded(page, "setup", width, scale);
      } finally {
        await context.close();
      }
    }
  }
});

test("workspace: a long title, the queue and the connection row fit at 320/390/768/1280, at 100% and 200% text (ULI-L05, ULI-L07, ULI-L09, ULI-L10)", async ({ browser, baseURL }) => {
  for (const scale of [1, 2]) {
    for (const width of CONTRACT_WIDTHS) {
      const context = await browser.newContext({ baseURL, viewport: { width, height: 900 } });
      const page = await context.newPage();
      try {
        await openConnected(page, [LONG, "Call the accountant"]);
        if (scale === 2) await scaleDefaultText(page, 2);
        await expectNoOverflowRecorded(page, "workspace", width, scale);
        if (scale === 1) {
          await expectRowOrStack(page, "connection-row", width);
          // The connection row spans the workspace like the queue above it.
          const [row, queue] = await Promise.all([page.locator(".connected").boundingBox(), page.getByRole("region", { name: "Needs your attention" }).boundingBox()]);
          const delta = Math.abs(row.x + row.width - (queue.x + queue.width));
          await record({ kind: "surface", id: "workspace", check: "connection row end edge", width }, { ruleId: "ULI-L04", status: delta <= 1 ? STATUS.PASS : STATUS.FAIL, reason: `end edges differ by ${delta.toFixed(1)} CSS px` });
          expect(delta, `connection row end vs queue end @${width}`).toBeLessThanOrEqual(1);
        }
      } finally {
        await context.close();
      }
    }
  }
});

test("print: the open items print as Folio's document, and a PDF renders (ULI-L12)", async ({ page }) => {
  await openConnected(page, [LONG, "Call the accountant"]);
  await page.emulateMedia({ media: "print" });
  const printed = page.locator(".print-surface");
  await expect(printed).toBeVisible();
  await expect(page.locator(".print-item")).toHaveCount(2);
  await expect(page.getByRole("textbox", { name: "What is open?" })).toBeHidden();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  const pdf = await page.pdf({ format: "Letter" });
  const ok = overflow <= 0 && pdf.subarray(0, 5).toString() === "%PDF-" && pdf.length > 1000;
  await record({ kind: "surface", id: "print", check: "print media and PDF", width: null }, {
    ruleId: "ULI-L12", status: ok ? STATUS.PASS : STATUS.FAIL,
    reason: `print surface shown with 2 items; screen controls hidden; horizontal overflow ${overflow}px; Letter PDF ${pdf.length} bytes`
  });
  expect(ok).toBe(true);
});
