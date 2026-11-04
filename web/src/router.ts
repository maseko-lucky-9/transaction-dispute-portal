import { createBrowserHistory, createRouter, type RouterHistory } from "@tanstack/react-router";

import { routeTree } from "./routeTree";

export function createAppRouter(history: RouterHistory = createBrowserHistory()) {
  return createRouter({ routeTree, history });
}

declare module "@tanstack/react-router" {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
}

declare module "@tanstack/history" {
  /**
   * One-shot messages carried in history state (never in the URL). History state SURVIVES a
   * reload and Back/Forward, so whoever shows one must wipe it from the entry once it is on
   * screen: see SubmissionNotice in dispute-detail-page.tsx.
   */
  interface HistoryState {
    disputeSubmitted?: boolean;
    /** The create was answered by a replay whose stored details differ from what was just sent. */
    earlierDetailsKept?: boolean;
  }
}
