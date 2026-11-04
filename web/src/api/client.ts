import { ApiError, networkError, toApiError } from "./problem";

/**
 * The only way the SPA talks to the backend. It owns three security properties:
 *
 *  - `X-CSRF: 1` on EVERY request. The API rejects /api and non-GET /bff calls without it,
 *    and there is no CORS policy, so a cross-site form or script cannot send a custom header.
 *  - `credentials: 'same-origin'`: the `dp_session` HttpOnly cookie rides along, and nothing
 *    else. The SPA never sees a token.
 *  - Same-origin paths only: an absolute URL would send the CSRF header (and, for a same-site
 *    host, cookies) somewhere we do not control.
 */

export const CSRF_HEADER = "X-CSRF";

export interface ApiFetchInit extends Omit<RequestInit, "body" | "credentials" | "headers"> {
  headers?: HeadersInit;
  /** Serialised as JSON with `Content-Type: application/json`. */
  json?: unknown;
}

function resolveSameOrigin(path: string): URL {
  const base = window.location.origin;
  const url = new URL(path, base);
  if (url.origin !== base || !path.startsWith("/") || path.startsWith("//")) {
    throw new Error(`apiFetch only accepts same-origin paths, got "${path}"`);
  }
  return url;
}

/**
 * Fetch `path` and return the parsed JSON body (`undefined` for 204).
 * Throws `ApiError` for non-2xx responses (parsed from problem details) and network failures.
 */
export async function apiFetch<T = unknown>(path: string, init: ApiFetchInit = {}): Promise<T> {
  const { json, headers: initHeaders, ...rest } = init;
  const url = resolveSameOrigin(path);

  const headers = new Headers(initHeaders);
  headers.set("Accept", "application/json");
  if (json !== undefined) headers.set("Content-Type", "application/json");
  // Set last so a caller cannot weaken it.
  headers.set(CSRF_HEADER, "1");

  let response: Response;
  try {
    response = await fetch(url, {
      ...rest,
      headers,
      credentials: "same-origin",
      ...(json !== undefined ? { body: JSON.stringify(json) } : {}),
    });
  } catch (cause) {
    // Let callers (React Query) cancel without it being reported as a network failure.
    if (rest.signal?.aborted || (cause instanceof Error && cause.name === "AbortError"))
      throw cause;
    throw networkError(cause);
  }

  if (!response.ok) throw await toApiError(response);
  if (response.status === 204) return undefined as T;

  try {
    return (await response.json()) as T;
  } catch (cause) {
    throw new ApiError(
      response.status,
      {
        title: "Unexpected response",
        detail: "The server sent a response we could not read.",
        status: response.status,
      },
      { cause },
    );
  }
}
