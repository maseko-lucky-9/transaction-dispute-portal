import { describe, expect, it } from "vitest";

import { ApiError, networkError } from "@/api/problem";

import { classifyCreateError } from "./dispute-errors";

const api = (
  status: number,
  problem: Record<string, unknown> = {},
  headers: Record<string, string> = {},
) => new ApiError(status, { status, ...problem }, { headers: new Headers(headers) });

describe("classifyCreateError", () => {
  it("maps 400 errors{} to fields, case-insensitively, first message each", () => {
    const failure = classifyCreateError(
      api(400, {
        code: "validation_failed",
        errors: {
          Description: ["Too short.", "Second message."],
          reasonCode: ["Unknown reason."],
          AMOUNT: ["Bad amount."],
        },
      }),
    );
    expect(failure).toEqual({
      kind: "fields",
      fields: { description: "Too short.", reasonCode: "Unknown reason.", amount: "Bad amount." },
      other: [],
    });
  });

  it("keeps messages about fields the form does not have", () => {
    const failure = classifyCreateError(
      api(400, {
        code: "validation_failed",
        errors: { transactionId: ["Bad id."], id: ["Also bad."] },
      }),
    );
    expect(failure).toEqual({ kind: "fields", fields: {}, other: ["Bad id.", "Also bad."] });
  });

  it("reads sensitive_data as a description message in our words", () => {
    expect(
      classifyCreateError(
        api(400, { code: "sensitive_data", errors: { description: ["Remove numbers."] } }),
      ),
    ).toEqual({
      kind: "fields",
      fields: { description: "Please remove card or ID numbers" },
      other: [],
    });
    expect(classifyCreateError(api(400, { code: "sensitive_data" }))).toEqual({
      kind: "fields",
      fields: { description: "Please remove card or ID numbers" },
      other: [],
    });
  });

  it.each([
    {},
    { errors: {} },
    { errors: "nope" },
    { errors: { description: [] } },
    { errors: { description: [1, ""] } },
  ])("treats a 400 with nothing usable (%j) as a plain failure", (extra) => {
    expect(classifyCreateError(api(400, { code: "validation_failed", ...extra }))).toEqual({
      kind: "failed",
    });
  });

  it("turns 422 violations into plain sentences, unique, with the server's words only for unknown codes", () => {
    const failure = classifyCreateError(
      api(422, {
        code: "eligibility_failed",
        violations: [
          { code: "window_expired", message: "ignored" },
          { code: "window_expired", message: "again" },
          { code: "amount_exceeds_transaction", message: "ignored" },
          { code: "new_rule", message: "A new rule." },
          "junk",
          { message: 5 },
        ],
      }),
    );
    expect(failure).toEqual({
      kind: "violations",
      messages: [
        "The 120-day dispute window has closed",
        "The amount is more than the transaction",
        "A new rule.",
        "This transaction can't be disputed",
      ],
    });
  });

  it("never shows an empty list for a 422", () => {
    expect(classifyCreateError(api(422, { code: "eligibility_failed" }))).toEqual({
      kind: "violations",
      messages: ["This transaction can't be disputed"],
    });
  });

  it("reads 409 dispute_exists with the existing id, and tolerates a missing one", () => {
    expect(
      classifyCreateError(api(409, { code: "dispute_exists", existingDisputeId: "abc" })),
    ).toEqual({
      kind: "exists",
      existingDisputeId: "abc",
    });
    expect(classifyCreateError(api(409, { code: "dispute_exists" }))).toEqual({
      kind: "exists",
      existingDisputeId: null,
    });
    expect(classifyCreateError(api(409, { code: "dispute_exists", existingDisputeId: 7 }))).toEqual(
      {
        kind: "exists",
        existingDisputeId: null,
      },
    );
  });

  it.each(["idempotency_key_reuse", "dispute_id_conflict"])(
    "reads 409 %s as an id conflict",
    (code) => {
      expect(classifyCreateError(api(409, { code }))).toEqual({ kind: "idConflict" });
    },
  );

  it("reads Retry-After as whole seconds only", () => {
    expect(classifyCreateError(api(429, {}, { "Retry-After": "30" }))).toEqual({
      kind: "rateLimited",
      retryAfterSeconds: 30,
    });
    expect(classifyCreateError(api(429, {}, { "Retry-After": " 5 " }))).toEqual({
      kind: "rateLimited",
      retryAfterSeconds: 5,
    });
    for (const value of ["", "soon", "-3", "1.5", "Wed, 21 Oct 2026 07:28:00 GMT"]) {
      expect(classifyCreateError(api(429, {}, { "Retry-After": value }))).toEqual({
        kind: "rateLimited",
        retryAfterSeconds: null,
      });
    }
    expect(classifyCreateError(api(429))).toEqual({ kind: "rateLimited", retryAfterSeconds: null });
  });

  it("maps 404, and calls everything else a failure worth retrying", () => {
    expect(classifyCreateError(api(404))).toEqual({ kind: "notFound" });
    for (const status of [401, 403, 409, 500, 502, 503]) {
      expect(classifyCreateError(api(status, { code: "other" }))).toEqual({ kind: "failed" });
    }
    expect(classifyCreateError(networkError(new TypeError("fetch failed")))).toEqual({
      kind: "failed",
    });
    expect(classifyCreateError(new Error("boom"))).toEqual({ kind: "failed" });
    expect(classifyCreateError("nope")).toEqual({ kind: "failed" });
  });
});
