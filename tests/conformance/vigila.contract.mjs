// Vigila's declared UI conformance contract (GH-36, vigila#36) against
// visual-engineering/UI-LAYOUT-INTERACTION@1.0.0 (ULI-L01..L13, ULI-A01..A10;
// cited, never restated).
//
// Pure data. Every enabled action the page ships is declared with its owner,
// the state that shows it, its intent and the outcome a person can observe.
// `verify` names where the outcome is asserted: "conformance" (this suite,
// through the vendored Forma helpers), { spec, title } (an existing browser
// test that asserts it, counted only if it passed in the same run), or null
// (unknown). The inventory test fails on any enabled action not declared here.

const SPEC = {
  capture: "tests/browser/capture.spec.js",
  connection: "tests/browser/connection.spec.js"
};

const existing = (spec, title) => ({ spec, title });

const action = (id, role, name, intent, expectedOutcome, { owner, state, verify = null, pattern, reset = "fresh page per test" } = {}) => ({
  id, control: { role, name }, intent, expectedOutcome, owner, state, verify, pattern, reset
});

export const SURFACES = [
  {
    id: "setup",
    path: "web/index.html (not connected: Connect to a repository)",
    kind: "product",
    actions: [
      action("setup.connect", "button", "Connect", "Validate the token, repository and branch against GitHub and open the application",
        "The capture form (\"What is open?\") replaces setup; a refused token, repository or branch is reported in an alert and setup stays",
        { owner: "engine: connect (Vigila.Application Dispatch)", state: "not connected, a token entered", verify: "conformance" })
    ],
    groups: []
  },
  {
    id: "workspace",
    path: "web/index.html (connected: capture, open items, connection)",
    kind: "product",
    actions: [
      action("workspace.capture", "button", "Capture", "Capture an open item", "The item is listed under \"Needs your attention\" and the draft clears",
        { owner: "engine: capture", state: "connected, a legal title typed", verify: "conformance" }),
      action("workspace.disconnect", "button", "Forget token / Disconnect", "Forget the token and leave the repository", "Setup (\"Connect to a repository\") replaces the application; the token is removed from storage",
        { owner: "engine: disconnect, connection.js token store", state: "connected", verify: "conformance" })
    ],
    groups: [
      { id: "connection-row", relation: "centerline", targets: [{ role: "text", name: "Connected" }, { role: "button", name: "Forget token / Disconnect" }],
        rationale: "The connection's status and its action share a centre line (ULI-L07, ULI-L10), or wrap in order." }
    ]
  },
  {
    id: "print",
    path: "web/index.html (print media: Folio open-items document)",
    kind: "print",
    actions: [],
    groups: []
  }
];

export const CONTRACT_WIDTHS = [320, 390, 768, 1280];

const matches = (declared, { role, name }) =>
  declared.control.role === role && (declared.pattern ? declared.pattern.test(name) : declared.control.name === name);

export const declaredActionFor = (control) =>
  SURFACES.flatMap((surface) => surface.actions).find((declared) => matches(declared, control)) ?? null;

export const actionById = (id) => SURFACES.flatMap((surface) => surface.actions).find((declared) => declared.id === id);
export const groupById = (id) => SURFACES.flatMap((surface) => surface.groups).find((group) => group.id === id);

export { SPEC, existing };
