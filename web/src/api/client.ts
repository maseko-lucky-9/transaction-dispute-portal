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

/** A successful response: the parsed body plus what the headers carry (ETag, Location, ...). */
export interface ApiResponse<T> {
  data: T;
  status: number;
  headers: Headers;
}

/**
 * Fetch `path` and return the parsed JSON body together with the status and headers
 * (`data` is `undefined` for 204). Use this when a header matters, e.g. the ETag that
 * optimistic concurrency needs. Throws `ApiError` for non-2xx responses (parsed from problem
 * details, with the response headers attached) and for network failures.
 */
export async function apiRequest<T = unknown>(
  path: string,
  init: ApiFetchInit = {},
): Promise<ApiResponse<T>> {
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
  if (response.status === 204) {
    return { data: undefined as T, status: response.status, headers: response.headers };
  }

  try {
    return {
      data: (await response.json()) as T,
      status: response.status,
      headers: response.headers,
    };
  } catch (cause) {
    throw new ApiError(
      response.status,
      {
        title: "Unexpected response",
        detail: "The server sent a response we could not read.",
        status: response.status,
      },
      { cause, headers: response.headers },
    );
  }
}

/** Like `apiRequest`, for callers that only want the parsed body. */
export async function apiFetch<T = unknown>(path: string, init: ApiFetchInit = {}): Promise<T> {
  return (await apiRequest<T>(path, init)).data;
}
