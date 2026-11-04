import { queryOptions, useQuery, type QueryClient } from "@tanstack/react-query";
import { z } from "@/lib/zod";

import { isSameOriginUrl, navigate } from "@/lib/navigation";

import { apiFetch } from "./client";
import { isApiError } from "./problem";
import type { User } from "./types";

// `satisfies` keeps this schema and the hand-written `User` type from drifting apart.
const userSchema = z.object({
  name: z.string(),
  roles: z.array(z.string()),
  // Logout is a top-level navigation: never follow a server-supplied URL off this origin.
  logoutUrl: z.string().refine(isSameOriginUrl, "logoutUrl must be same-origin"),
}) satisfies z.ZodType<User>;

export const sessionQueryKey = ["session"] as const;

/**
 * `GET /bff/user`. Resolves to the user, or to `null` when there is no session (401).
 * "Not signed in" is a normal state, not an error; every other failure throws `ApiError`.
 */
export async function fetchSession(): Promise<User | null> {
  let body: unknown;
  try {
    body = await apiFetch("/bff/user");
  } catch (error) {
    if (isApiError(error) && error.status === 401) return null;
    throw error;
  }
  const parsed = userSchema.safeParse(body);
  if (!parsed.success) throw new Error("Unexpected response from /bff/user");
  return parsed.data;
}

export const sessionQueryOptions = queryOptions({
  queryKey: sessionQueryKey,
  queryFn: fetchSession,
  staleTime: 30_000,
  // A failed session check is shown immediately with a Retry button, not retried silently.
  retry: false,
});

export function useSession() {
  return useQuery(sessionQueryOptions);
}

/**
 * Sign out. The Query cache holds the signed-in user's data, so it is cleared BEFORE the
 * browser leaves; the navigation then drops all in-memory state anyway.
 */
export function logout(queryClient: QueryClient, logoutUrl: string): void {
  queryClient.clear();
  navigate(logoutUrl);
}
