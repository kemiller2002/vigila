// Limen kernel side - the GitHub token, and nothing else.
//
// This module exists so the token never crosses into the engine. It owns the
// credential end to end: reading it from localStorage, writing it there, giving
// it to the transport for outbound GitHub requests, and forgetting it.
//
// What the engine is told is one bit — whether a token exists — carried by the
// `vigila.tokenPresent` flag this module maintains beside the credential, and
// by the `tokenEntered` event it fires while the user types. Neither is the
// token.
//
// That is what makes VIG-SEC-005 structural. The requirement names thirteen
// places the token must never appear: logs, receipts, command envelopes,
// telemetry, pagination cursors, exports, rendered HTML, URLs, and so on. All
// of them are produced on the far side of this file, so a token that never goes
// there cannot reach any of them, and no future code path has to remember.
//
// Requirements: VIG-SEC-002, VIG-SEC-005, VIG-SEC-006, VIG-SEC-020.

const TOKEN_KEY = "vigila.token";
const PRESENCE_KEY = "vigila.tokenPresent";

// The one origin a stored credential may ever be sent to (VIG-SEC-005: "sent
// anywhere other than the GitHub APIs required for application operation").
// Checked by parsed origin rather than by prefix, so a URL like
// https://api.github.com.example.com/ does not match.
const GITHUB_API_ORIGIN = "https://api.github.com";

// localStorage throws rather than returning null in some privacy modes, so
// every access is guarded and a failure reads as "no token".
const read = (key) => {
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
};

const write = (key, value) => {
  try {
    window.localStorage.setItem(key, value);
    return true;
  } catch {
    return false;
  }
};

const drop = (key) => {
  try {
    window.localStorage.removeItem(key);
  } catch {
    // Nothing useful to do, and nothing to report: the caller's next read
    // returns whatever is actually there.
  }
};

export class TokenStore {
  /** The live token, or null. Never returned to, or rendered for, the engine. */
  #token = read(TOKEN_KEY);

  get hasToken() {
    return typeof this.#token === "string" && this.#token !== "";
  }

  /**
   * Stores a token, or clears it when given an empty value.
   *
   * The presence flag is written in the same call as the credential, so the two
   * cannot drift: the engine's view of "is there a token" is maintained by the
   * only code that can change the answer.
   */
  set(token) {
    const value = typeof token === "string" ? token.trim() : "";

    if (value === "") {
      this.forget();
      return;
    }

    this.#token = value;
    write(TOKEN_KEY, value);
    write(PRESENCE_KEY, "yes");
  }

  /** VIG-SEC-006: forgetting the token must not touch repository data. */
  forget() {
    this.#token = null;
    drop(TOKEN_KEY);
    drop(PRESENCE_KEY);
  }

  /**
   * Adds the credential to a request the engine asked for.
   *
   * The engine never supplies an Authorization header, so there is nothing to
   * override: this is the only place one is created. A request to any origin
   * but GitHub's API is returned untouched, which means an engine bug, or a
   * repository name crafted to look like a URL, cannot redirect the credential
   * somewhere else.
   */
  authorize(url, headers) {
    if (!this.hasToken) return headers;

    let origin;
    try {
      origin = new URL(url).origin;
    } catch {
      return headers;
    }

    if (origin !== GITHUB_API_ORIGIN) return headers;

    return {
      ...headers,
      Authorization: `Bearer ${this.#token}`,
      Accept: "application/vnd.github+json",
    };
  }
}

/**
 * Wires the credential's whole lifecycle to the DOM.
 *
 * Every listener is delegated from the document rather than bound to an
 * element, because the setup form lives inside a `data-if` template: it is
 * removed when the connection succeeds and a *new* element is created when the
 * user disconnects. Listeners attached to the original would be thrown away
 * with it, silently, and the second visit to the form would do nothing.
 *
 * The engine drives application state on connect and disconnect. This drives
 * the credential, because the engine cannot: it does not know the key the token
 * is stored under, which is the entire point.
 */
export function bindCredential(doc, store) {
  const presenceOf = () => doc.getElementById("token-present");
  const fieldOf = () => doc.getElementById("token");

  // Presence travels the same route every other event does — a bound element
  // whose value changes — because the kernel exposes no way to send a semantic
  // event directly. That value is "yes" or "", never the token.
  const announce = () => {
    const presence = presenceOf();
    if (!presence) return;

    const field = fieldOf();
    const typed = field ? field.value.trim() !== "" : false;
    const next = typed || store.hasToken ? "yes" : "";

    if (presence.value === next) return;
    presence.value = next;
    presence.dispatchEvent(new Event("input", { bubbles: true }));
  };

  doc.addEventListener("input", (event) => {
    if (event.target === fieldOf()) announce();
  });

  // Capture phase: the store must hold the token before the engine's connect
  // command produces the request that needs it.
  doc.addEventListener(
    "submit",
    () => {
      const field = fieldOf();
      if (!field || field.value.trim() === "") return;

      store.set(field.value);
      field.value = "";
      announce();
    },
    true,
  );

  // "Forget token / Disconnect" must actually forget the token. The engine
  // clears the application's state and the presence flag; only this side can
  // remove the credential itself (VIG-SEC-006).
  doc.addEventListener(
    "click",
    (event) => {
      const target = event.target;
      if (!(target instanceof Element)) return;
      if (!target.closest('[data-event="disconnect"]')) return;

      store.forget();
      announce();
    },
    true,
  );

  // The form may not exist yet on the first call, and may be replaced later;
  // announcing now covers the case where a stored token is already present.
  announce();

  // A remounted form starts empty, so its presence must be re-announced once it
  // appears. MutationObserver is the only signal Limen gives for that.
  const observer = new MutationObserver(() => announce());
  observer.observe(doc.body, { childList: true, subtree: true });
}
