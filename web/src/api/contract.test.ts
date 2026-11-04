import { describe, expect, inject, it } from "vitest";

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
interface OpenApiSubset {
  paths: Record<string, { get?: { parameters?: { name: string; schema: ParameterSchema }[] } }>;
  components: { schemas: Record<string, { enum?: string[] }> };
}

// A web-only checkout has no docs/ next to it; CI and the full repo always do.
describe.skipIf(specText === undefined)("client constants match docs/openapi.json", () => {
  const spec = JSON.parse(specText ?? "{}") as OpenApiSubset;
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
