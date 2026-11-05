import { createMemoryHistory, type RouterHistory } from "@tanstack/react-router";
import { render } from "@testing-library/react";
import { StrictMode } from "react";

import { createQueryClient } from "@/api/query-client";
import { App } from "@/app";
import { createAppRouter } from "@/router";

/**
 * Render the real app (router + query client + shell + gate) at `path`.
 * Only the network is faked (MSW) and `navigate` is mocked by tests that need it.
 */
export function renderApp(
  path = "/",
  { strict = false, history }: { strict?: boolean; history?: RouterHistory } = {},
) {
  const queryClient = createQueryClient();
  // Same factory as production, minus retries/stale time so failures show immediately.
  queryClient.setDefaultOptions({ queries: { retry: false, staleTime: 0 } });
  // A test that needs the REAL window.history (state surviving a "reload") passes its own.
  const router = createAppRouter(history ?? createMemoryHistory({ initialEntries: [path] }));
  const app = <App queryClient={queryClient} router={router} />;
  // main.tsx renders under StrictMode, which double-invokes initialisers and re-runs effects.
  const view = render(strict ? <StrictMode>{app}</StrictMode> : app);
  return { ...view, queryClient, router };
}
