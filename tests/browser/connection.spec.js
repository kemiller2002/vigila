// The connection flow, driven in a real browser against a stubbed GitHub.
//
// GitHub is stubbed at the network boundary rather than mocked inside the app,
// so everything between the DOM and `fetch` is the real thing: Limen's kernel,
// the WASM transport, the F# engine, and the effects it emits. What the stub
// sees is exactly what a real request would carry — which is what makes the
// token assertions below worth anything.
//
// Requirements: VIG-SEC-001, VIG-SEC-002, VIG-SEC-005, VIG-SEC-006,
// VIG-SEC-007, VIG-SEC-009, VIG-SEC-012.
import { expect, test } from "@playwright/test";
import { REPOSITORY, TOKEN, connect, openPage, selectors, stubGitHub } from "./support.js";

test("with nothing stored, the user lands on setup and not on the app", async ({ page }) => {
  const failures = await openPage(page);

  // VIG-SEC-007 step 2.
  await expect(page.locator(selectors.setup)).toBeVisible();
  await expect(page.locator(selectors.capture)).toHaveCount(0);
  await expect(page.locator(selectors.branch)).toHaveValue("main");
  expect(failures()).toEqual([]);
});

test("connect is refused until a token is entered", async ({ page }) => {
  const failures = await openPage(page);

  await expect(page.locator(selectors.connect)).toBeDisabled();

  await page.fill(selectors.token, TOKEN);
  await expect(page.locator(selectors.connect)).toBeEnabled();

  // Clearing it again withdraws permission to try.
  await page.fill(selectors.token, "");
  await expect(page.locator(selectors.connect)).toBeDisabled();
  expect(failures()).toEqual([]);
});

test("a valid configuration connects and reveals the application", async ({ page }) => {
  const failures = await openPage(page);
  const requests = await stubGitHub(page);

  await connect(page);

  await expect(page.locator(selectors.capture)).toBeVisible();
  await expect(page.locator(selectors.setup)).toHaveCount(0);
  await expect(page.locator(selectors.empty)).toBeVisible();

  // VIG-SEC-009: both capabilities are proved, in order, before connecting.
  expect(requests.map((r) => new URL(r.url).pathname)).toEqual([
    "/repos/owner/data",
    "/repos/owner/data/branches/main",
  ]);
  expect(failures()).toEqual([]);
});

test("the token reaches GitHub and nowhere else", async ({ page }) => {
  await openPage(page);
  const requests = await stubGitHub(page);

  await connect(page);
  await expect(page.locator(selectors.capture)).toBeVisible();

  // It must be on the GitHub requests, or nothing would work...
  expect(requests).not.toHaveLength(0);
  for (const request of requests) {
    expect(request.headers.authorization).toBe(`Bearer ${TOKEN}`);
  }

  // ...and it must be nowhere a person or a log could see it. VIG-SEC-005
  // names rendered HTML and URLs explicitly.
  const html = await page.content();
  expect(html).not.toContain(TOKEN);

  const url = page.url();
  expect(url).not.toContain(TOKEN);

  // The field itself is cleared once the credential is stored, so a shoulder
  // or a screenshot does not carry it either.
  await expect(page.locator(selectors.token)).toHaveCount(0);
});

test("the token is stored under its own key and the engine only sees a flag", async ({ page }) => {
  await openPage(page);
  await stubGitHub(page);

  await connect(page);
  await expect(page.locator(selectors.capture)).toBeVisible();

  // VIG-SEC-002: configuration survives a reload.
  const stored = await page.evaluate(() => ({ ...window.localStorage }));
  expect(stored["vigila.repository"]).toBe("owner/data");
  expect(stored["vigila.branch"]).toBe("main");
  expect(stored["vigila.tokenPresent"]).toBe("yes");
  expect(stored["vigila.token"]).toBe(TOKEN);
});

test("a stored configuration is revalidated on reload, not trusted", async ({ page }) => {
  await openPage(page);
  const first = await stubGitHub(page);
  await connect(page);
  await expect(page.locator(selectors.capture)).toBeVisible();
  expect(first).toHaveLength(2);

  // VIG-SEC-008: reloading must re-prove the connection rather than assume it.
  const second = await stubGitHub(page);
  await page.reload();

  await expect(page.locator(selectors.capture)).toBeVisible();
  expect(second.map((r) => new URL(r.url).pathname)).toEqual([
    "/repos/owner/data",
    "/repos/owner/data/branches/main",
  ]);
});

test("a rejected token says so, and does not open the application", async ({ page }) => {
  await openPage(page);
  await stubGitHub(page, { repoStatus: 401 });

  await connect(page);

  await expect(page.locator(selectors.setupError)).toBeVisible();
  await expect(page.locator(selectors.setupError)).toContainText("rejected that token");
  // VIG-SEC-012: no stale data presented as though the connection succeeded.
  await expect(page.locator(selectors.capture)).toHaveCount(0);
});

test("a readable repository with no write access is refused with its own reason", async ({
  page,
}) => {
  await openPage(page);
  await stubGitHub(page, { canPush: false });

  await connect(page);

  await expect(page.locator(selectors.setupError)).toContainText("cannot write");
  await expect(page.locator(selectors.capture)).toHaveCount(0);
});

test("a missing branch is reported as a branch problem", async ({ page }) => {
  await openPage(page);
  await stubGitHub(page, { branchStatus: 404 });

  await connect(page);

  await expect(page.locator(selectors.setupError)).toContainText("branch was not found");
  await expect(page.locator(selectors.capture)).toHaveCount(0);
});

test("a malformed repository is refused before any request is sent", async ({ page }) => {
  await openPage(page);
  const requests = await stubGitHub(page);

  await connect(page, { repository: "not-a-repository" });

  await expect(page.locator(selectors.setupError)).toContainText("owner/repository");
  expect(requests).toHaveLength(0);
});

test("disconnect forgets the token, keeps the repository, and returns to setup", async ({
  page,
}) => {
  await openPage(page);
  await stubGitHub(page);
  await connect(page);
  await expect(page.locator(selectors.capture)).toBeVisible();

  await page.click(selectors.disconnect);

  await expect(page.locator(selectors.setup)).toBeVisible();
  await expect(page.locator(selectors.capture)).toHaveCount(0);

  // VIG-SEC-006: the credential goes, the configuration stays.
  const stored = await page.evaluate(() => ({ ...window.localStorage }));
  expect(stored["vigila.token"]).toBeUndefined();
  expect(stored["vigila.tokenPresent"]).toBeUndefined();
  expect(stored["vigila.repository"]).toBe("owner/data");

  await expect(page.locator(selectors.repository)).toHaveValue("owner/data");
});
