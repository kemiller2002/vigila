// End-to-end configuration for the composed browser path.
//
// The .NET tests prove the engine decides correctly. They cannot prove the
// engine reaches the browser: the [JSExport] shim, the WASM transport, Limen's
// kernel and the HTML bindings sit between the two and none of them is
// type-checked against the other. This config drives the real page in a real
// browser so that path is observed rather than assumed.
//
// Requirements: VIG-GOV-008, VIG-GOV-010, VIG-GOV-013.
// SDE: method/VERIFICATION-METHOD.md — "present-but-wrong implementation".
import { existsSync } from "node:fs";
import { defineConfig, devices } from "@playwright/test";

const port = 4321;
const origin = `http://127.0.0.1:${port}`;

// Some environments ship a Chromium that Playwright did not download itself and
// must not try to. Where that binary exists it is used as-is; everywhere else
// Playwright resolves its own, so CI needs no special case.
const preinstalledChromium = "/opt/pw-browsers/chromium";
const launch = existsSync(preinstalledChromium)
  ? { executablePath: preinstalledChromium }
  : {};

export default defineConfig({
  testDir: "./tests/browser",
  // The engine holds module-level state across dispatches, and one page per
  // test is the isolation boundary. Serial keeps the published engine's
  // startup cost paid once per worker rather than per parallel worker.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: process.env.CI ? [["github"], ["list"]] : [["list"]],
  use: {
    baseURL: origin,
    trace: "retain-on-failure",
  },
  projects: [
    { name: "chromium", use: { ...devices["Desktop Chrome"], launchOptions: launch } },
  ],
  // Served from the repository root, not from web/: index.html reaches up into
  // node_modules/ for Limen's kernel and into src/ for the published engine.
  webServer: {
    command: `python3 -m http.server ${port}`,
    url: `${origin}/web/index.html`,
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
  },
});
