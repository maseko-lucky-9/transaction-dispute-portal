import { MutationCache, QueryCache, QueryClient } from "@tanstack/react-query";

import { isApiError } from "./problem";
import { sessionQueryKey } from "./session";

/**
 * One QueryClient factory for the app and the tests.
 *
 * A 401 from ANY call means the session ended (15 min idle / 8 h absolute / refresh failed),
 * so the session query flips to "signed out" and the gate shows the Sign in screen instead of
 * every page rendering its own auth error.
 */
export function createQueryClient(): QueryClient {
  const onError = (error: unknown): void => {
    if (isApiError(error) && error.status === 401) {
      client.setQueryData(sessionQueryKey, null);
    }
  };

  const client: QueryClient = new QueryClient({
    queryCache: new QueryCache({ onError }),
    mutationCache: new MutationCache({ onError }),
    defaultOptions: {
      queries: {
        staleTime: 10_000,
        // Retry transient failures only; a 4xx will not get better by asking again.
        retry: (failureCount, error) =>
          failureCount < 2 && !(isApiError(error) && error.status >= 400 && error.status < 500),
      },
    },
  });
  return client;
}
