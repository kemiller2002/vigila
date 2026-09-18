// Shared setup for the browser suites.
//
// GitHub is stubbed at the network boundary, not mocked inside the app, so
// everything between the DOM and `fetch` stays real: Limen's kernel, the WASM
// transport, the F# engine, and the effects it emits.
import { expect } from "@playwright/test";

export const PAGE = "/web/index.html";
export const TOKEN = "ghp_exampletokenvalue0123456789";
export const REPOSITORY = "owner/data";

export const selectors = {
  setup: ".setup",
  token: "#token",
  repository: "#repository",
  branch: "#branch",
  connect: ".setup__submit",
  setupError: ".setup__error",
  capture: ".capture",
  disconnect: ".disconnect",
};

/**
 * Stubs api.github.com and returns the list of requests that reached it.
 *
 * The returned array is live: assertions read it after the interaction.
 */
export const stubGitHub = async (
  page,
  { repoStatus = 200, canPush = true, branchStatus = 200 } = {},
) => {
  const seen = [];

  await page.route("https://api.github.com/**", async (route) => {
    const request = route.request();
    seen.push({ url: request.url(), headers: request.headers() });

    if (/\/branches\//.test(request.url())) {
      await route.fulfill({
        status: branchStatus,
        contentType: "application/json",
        body: JSON.stringify({ name: "main" }),
      });
      return;
    }

    await route.fulfill({
      status: repoStatus,
      contentType: "application/json",
      body: JSON.stringify({
        full_name: REPOSITORY,
        permissions: { admin: false, push: canPush, pull: true },
      }),
    });
  });

  return seen;
};

/**
 * Opens the page and fails the test on any console error or uncaught exception.
 *
 * A silently dead engine renders an empty page and passes a naive assertion; it
 * does not pass this. That is not hypothetical — it is how the engine shipped
 * broken once already.
 */
export const openPage = async (page) => {
  const failures = [];

  page.on("console", (message) => {
    if (message.type() === "error") failures.push(`console: ${message.text()}`);
  });
  page.on("pageerror", (error) => failures.push(`pageerror: ${error.message}`));

  await page.goto(PAGE);
  await expect(page.locator(selectors.setup)).toBeVisible();

  return () => failures;
};

export const connect = async (page, { token = TOKEN, repository = REPOSITORY } = {}) => {
  await page.fill(selectors.token, token);
  await page.fill(selectors.repository, repository);
  await page.click(selectors.connect);
};

/** Opens the page and connects, leaving the application visible. */
export const openConnected = async (page, options) => {
  const failures = await openPage(page);
  const requests = await stubGitHub(page, options);

  await connect(page);
  await expect(page.locator(selectors.capture)).toBeVisible();

  return { failures, requests };
};
