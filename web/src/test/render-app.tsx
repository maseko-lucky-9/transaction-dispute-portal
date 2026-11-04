import { createMemoryHistory } from "@tanstack/react-router";
import { render } from "@testing-library/react";

import { createQueryClient } from "@/api/query-client";
import { App } from "@/app";
import { createAppRouter } from "@/router";

/**
 * Render the real app (router + query client + shell + gate) at `path`.
 * Only the network is faked (MSW) and `navigate` is mocked by tests that need it.
 */
export function renderApp(path = "/") {
  const queryClient = createQueryClient();
  // Same factory as production, minus retries/stale time so failures show immediately.
  queryClient.setDefaultOptions({ queries: { retry: false, staleTime: 0 } });
  const router = createAppRouter(createMemoryHistory({ initialEntries: [path] }));
  const view = render(<App queryClient={queryClient} router={router} />);
  return { ...view, queryClient, router };
}
