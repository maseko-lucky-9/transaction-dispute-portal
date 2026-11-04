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
