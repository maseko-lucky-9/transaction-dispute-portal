import { describe, expect, inject, it } from "vitest";

import { DISPUTE_STATUSES } from "@/lib/dispute-filters";
import { DESCRIPTION_MAX, DESCRIPTION_MIN } from "@/lib/dispute-form";
import { normalizeAmount } from "@/lib/decimal";
import {
  DEFAULT_LIMIT,
  MAX_LIMIT,
  MIN_LIMIT,
  TRANSACTION_STATUSES,
} from "@/lib/transaction-filters";

/**
 * Pins the client's runtime constants to the committed OpenAPI snapshot. Types cannot carry
 * numbers, so `gen:types` alone would not notice the API changing `limit` from 1..100 to
 * 1..50: this test does. The snapshot is read by vite.config.ts (docs/ is not in the web
 * image's build context, and application code never needs it) and injected here.
 */
const specText = inject("openApiSpec");

interface ParameterSchema {
  type?: string;
  format?: string;
  minimum?: number;
  maximum?: number;
  default?: number;
  enum?: string[];
}
interface SchemaProperty {
  minLength?: number;
  maxLength?: number;
  pattern?: string;
}
interface OpenApiSubset {
  paths: Record<string, { get?: { parameters?: { name: string; schema: ParameterSchema }[] } }>;
  components: {
    schemas: Record<string, { enum?: string[]; properties?: Record<string, SchemaProperty> }>;
  };
}

// `describe.skipIf` still RUNS the describe body (to register the skipped tests), so the body
// must not crash on a missing spec: without docs/ it reads this empty one and every test skips.
const spec: OpenApiSubset =
  specText == null
    ? { paths: {}, components: { schemas: {} } }
    : (JSON.parse(specText) as OpenApiSubset);

// A web-only checkout has no docs/ next to it: vite.config.ts then provides null (and an
// unconfigured runner would give undefined), so skip on either. CI and the full repo always run it.
describe.skipIf(specText == null)("client constants match docs/openapi.json", () => {
  const parameters = Object.fromEntries(
    (spec.paths["/api/v1/transactions"]?.get?.parameters ?? []).map((p) => [p.name, p.schema]),
  );

  it("describes the list endpoint's parameters at all", () => {
    expect(Object.keys(parameters).sort()).toEqual(["cursor", "from", "limit", "status", "to"]);
  });

  it("limit: integer, same minimum, maximum and default as the client", () => {
    expect(parameters["limit"]).toMatchObject({
      type: "integer",
      minimum: MIN_LIMIT,
      maximum: MAX_LIMIT,
      default: DEFAULT_LIMIT,
    });
  });

  it("from and to are dates", () => {
    expect(parameters["from"]).toMatchObject({ type: "string", format: "date" });
    expect(parameters["to"]).toMatchObject({ type: "string", format: "date" });
  });

  it("status: the query parameter and the response enum list exactly the client's statuses", () => {
    expect(parameters["status"]?.enum).toEqual([...TRANSACTION_STATUSES]);
    expect(spec.components.schemas["TransactionStatus"]?.enum).toEqual([...TRANSACTION_STATUSES]);
  });
});

describe.skipIf(specText == null)("dispute constants match docs/openapi.json", () => {
  const history = Object.fromEntries(
    (spec.paths["/api/v1/disputes"]?.get?.parameters ?? []).map((p) => [p.name, p.schema]),
  );
  const create = spec.components.schemas["CreateDisputeRequest"]?.properties ?? {};

  it("the history takes status, limit and cursor, with the same limit rules as transactions", () => {
    expect(Object.keys(history).sort()).toEqual(["cursor", "limit", "status"]);
    expect(history["limit"]).toMatchObject({
      type: "integer",
      minimum: MIN_LIMIT,
      maximum: MAX_LIMIT,
      default: DEFAULT_LIMIT,
    });
  });

  it("status: the query parameter and the response enum list exactly the client's statuses", () => {
    expect(history["status"]?.enum).toEqual([...DISPUTE_STATUSES]);
    expect(spec.components.schemas["DisputeStatus"]?.enum).toEqual([...DISPUTE_STATUSES]);
  });

  it("description: the form's length rule is the API's", () => {
    expect(create["description"]).toMatchObject({
      minLength: DESCRIPTION_MIN,
      maxLength: DESCRIPTION_MAX,
    });
  });

  it("amount: whatever the form sends satisfies the API's pattern", () => {
    const pattern = new RegExp(create["amount"]?.pattern ?? "(?!)");
    for (const typed of ["50", "50.5", "50,50", "0.01", "007", "9999999999999.99", " 7.10 "]) {
      expect(normalizeAmount(typed)).toMatch(pattern);
    }
    // ...and what the pattern would refuse, the form never produces.
    expect(pattern.test("10000000000000.00")).toBe(false);
    expect(normalizeAmount("10000000000000")).toBeNull();
    expect(pattern.test("1.234")).toBe(false);
    expect(normalizeAmount("1.234")).toBeNull();
  });
});
