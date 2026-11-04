import { isApiError } from "@/api/problem";
import { describeViolation } from "@/lib/dispute-reasons";
import { SENSITIVE_MESSAGE } from "@/lib/dispute-form";

export type DisputeField = "reasonCode" | "description" | "amount";

/** What a failed "submit dispute" means for the screen. One kind per way the user can act. */
export type CreateFailure =
  /** 400: messages belong beside the fields; `other` are about fields the form does not have. */
  | { kind: "fields"; fields: Partial<Record<DisputeField, string>>; other: string[] }
  /** 422: the transaction (or amount) breaks eligibility rules. */
  | { kind: "violations"; messages: string[] }
  /** 409 dispute_exists. */
  | { kind: "exists"; existingDisputeId: string | null }
  /** 409 idempotency_key_reuse / dispute_id_conflict: this id belongs to a different request. */
  | { kind: "idConflict" }
  | { kind: "notFound" }
  | { kind: "rateLimited"; retryAfterSeconds: number | null }
  /** 5xx, network, and anything unexpected: the same request may simply work next time. */
  | { kind: "failed" };

const FIELD_BY_NAME: Record<string, DisputeField> = {
  reasoncode: "reasonCode",
  description: "description",
  amount: "amount",
};

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === "object" && value !== null && !Array.isArray(value);

/** `errors: { field: [message, ...] }` -> the first message per field; the rest are unmapped. */
function readFieldErrors(errors: unknown): Extract<CreateFailure, { kind: "fields" }> {
  const result: Extract<CreateFailure, { kind: "fields" }> = {
    kind: "fields",
    fields: {},
    other: [],
  };
  if (!isRecord(errors)) return result;
  for (const [name, messages] of Object.entries(errors)) {
    const list = Array.isArray(messages)
      ? messages.filter((m): m is string => typeof m === "string" && m.trim() !== "")
      : [];
    const first = list[0];
    if (first === undefined) continue;
    // The server may spell a field "Description" or "description": compare without case.
    const field = FIELD_BY_NAME[name.toLowerCase()];
    if (field && result.fields[field] === undefined) result.fields[field] = first;
    else if (!field) result.other.push(...list);
  }
  return result;
}

function readViolations(violations: unknown): string[] {
  if (!Array.isArray(violations)) return [];
  const messages = violations.flatMap((violation: unknown) => {
    if (!isRecord(violation)) return [];
    const code = typeof violation["code"] === "string" ? violation["code"] : "";
    const message = typeof violation["message"] === "string" ? violation["message"] : undefined;
    return [describeViolation(code, message)];
  });
  return [...new Set(messages)];
}

/** Only "seconds" is understood; an HTTP-date is rare enough to answer with no number. */
function readRetryAfter(headers: Headers): number | null {
  const value = headers.get("Retry-After")?.trim() ?? "";
  return /^[0-9]+$/.test(value) ? Number(value) : null;
}

export function classifyCreateError(error: unknown): CreateFailure {
  if (!isApiError(error)) return { kind: "failed" };
  const { status, problem } = error;

  switch (status) {
    case 400: {
      const failure = readFieldErrors(problem["errors"]);
      // The sensitive-data rule is about the description even if the server sends no field.
      if (problem.code === "sensitive_data") failure.fields.description = SENSITIVE_MESSAGE;
      const empty = Object.keys(failure.fields).length === 0 && failure.other.length === 0;
      return empty ? { kind: "failed" } : failure;
    }
    case 404:
      return { kind: "notFound" };
    case 409: {
      if (problem.code === "dispute_exists") {
        const id = problem["existingDisputeId"];
        return { kind: "exists", existingDisputeId: typeof id === "string" ? id : null };
      }
      if (problem.code === "idempotency_key_reuse" || problem.code === "dispute_id_conflict") {
        return { kind: "idConflict" };
      }
      return { kind: "failed" };
    }
    case 422: {
      const messages = readViolations(problem["violations"]);
      return {
        kind: "violations",
        messages: messages.length > 0 ? messages : [describeViolation("")],
      };
    }
    case 429:
      return { kind: "rateLimited", retryAfterSeconds: readRetryAfter(error.headers) };
    default:
      return { kind: "failed" };
  }
}
