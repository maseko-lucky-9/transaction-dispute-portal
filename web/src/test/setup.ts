import "@testing-library/jest-dom/vitest";
import { cleanup, configure } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, beforeEach, expect, vi } from "vitest";
import * as axeMatchers from "vitest-axe/matchers";

import { server } from "./server";

expect.extend(axeMatchers);

// The first findBy* in a file also waits for a lazy route chunk to be imported and transformed,
// which takes well over the 1 s default on a busy machine (shared CI runner, a parallel build).
// Passing tests are no slower for this: it only bounds how long a failing one waits.
configure({ asyncUtilTimeout: 6000 });

// jsdom has no matchMedia. Default: light OS theme; tests use mockColorScheme() to change it.
// Re-stubbed per test because `unstubGlobals` restores globals after each one.
beforeEach(() => {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
});

// jsdom prints "Not implemented" for these. The router restores scroll on navigation and
// axe probes a canvas for icon ligatures; neither matters for what is asserted here.
window.scrollTo = () => undefined;
HTMLCanvasElement.prototype.getContext = () => null;

// Any request without a handler fails the test: nothing may escape to the real network.
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));

afterEach(() => {
  cleanup();
  server.resetHandlers();
  window.localStorage.clear();
  delete document.documentElement.dataset["theme"];
  window.history.replaceState({}, "", "/");
});

afterAll(() => server.close());
