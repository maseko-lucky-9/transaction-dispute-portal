import { z } from "zod";

/**
 * RFC 9457 problem details as the API emits them:
 * `type, title, status, detail, instance, code, traceId`, plus extension members
 * (`errors{}` on 400, `violations[]` on 422) that later pages read from the index signature.
 *
 * Every known member is optional and lenient: a wrong-typed member is dropped rather than
 * failing the whole parse, so the UI still gets a title and a status.
 */
const optionalString = z
  .string()
  .nullish()
  .catch(undefined)
  .transform((value) => value ?? undefined);

const optionalNumber = z
  .number()
  .nullish()
  .catch(undefined)
  .transform((value) => value ?? undefined);

const problemSchema = z.looseObject({
  type: optionalString,
  title: optionalString,
  status: optionalNumber,
  detail: optionalString,
  instance: optionalString,
  code: optionalString,
  traceId: optionalString,
});

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  code?: string;
  traceId?: string;
  /** Extension members: `errors{}` on 400, `violations[]` on 422. */
  [extension: string]: unknown;
}

/** Thrown for every non-2xx response and for network failures (status 0). */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails;

  constructor(status: number, problem: ProblemDetails, options?: ErrorOptions) {
    super(problem.detail ?? problem.title ?? `Request failed (${status})`, options);
    this.name = "ApiError";
    this.status = status;
    this.problem = problem;
  }

  /** Machine-readable error code, e.g. `dispute_exists`. */
  get code(): string | undefined {
    return this.problem.code;
  }

  /** Correlation id to quote to support; also the id of the server-side trace. */
  get traceId(): string | undefined {
    return this.problem.traceId;
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

/** Build the ApiError for a non-2xx response, tolerating bodies that are not problem JSON. */
export async function toApiError(response: Response): Promise<ApiError> {
  const status = response.status;
  const fallback: ProblemDetails = {
    title: response.statusText || `HTTP ${status}`,
    status,
  };

  const contentType = response.headers.get("content-type") ?? "";
  if (!contentType.includes("json")) return new ApiError(status, fallback);

  try {
    const parsed = problemSchema.safeParse(await response.json());
    if (!parsed.success) return new ApiError(status, fallback);
    // The HTTP status is authoritative over whatever the body claims.
    return new ApiError(status, { ...fallback, ...stripUndefined(parsed.data), status });
  } catch {
    return new ApiError(status, fallback);
  }
}

/** A network-level failure (offline, DNS, connection reset): there is no response at all. */
export function networkError(cause: unknown): ApiError {
  return new ApiError(
    0,
    {
      title: "Network error",
      detail: "Could not reach the server. Check your connection and try again.",
      code: "network_error",
      status: 0,
    },
    { cause },
  );
}

function stripUndefined(problem: ProblemDetails): ProblemDetails {
  return Object.fromEntries(Object.entries(problem).filter(([, value]) => value !== undefined));
}
