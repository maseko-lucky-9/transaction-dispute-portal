import { http, HttpResponse } from "msw";
import { describe, expect, expectTypeOf, it } from "vitest";

import type { TransactionStatus } from "@/lib/transaction-filters";
import { server } from "@/test/server";
import { makeTransaction, makeTransactions } from "@/test/transactions";

import { ApiError } from "./problem";
import type { components } from "./schema";
import {
  fetchTransactionPage,
  flattenPages,
  transactionsInfiniteOptions,
  transactionsKey,
} from "./transactions";

function captureRequest() {
  const seen: Request[] = [];
  server.use(
    http.get("/api/v1/transactions", ({ request }) => {
      seen.push(request);
      return HttpResponse.json({ items: [], nextCursor: null });
    }),
  );
  return seen;
}

describe("fetchTransactionPage", () => {
  it("sends only the limit when no filter is set", async () => {
    const seen = captureRequest();
    await fetchTransactionPage({ limit: 25 }, undefined);
    expect(new URL(seen[0]!.url).search).toBe("?limit=25");
  });

  it("sends every filter and the cursor, encoded", async () => {
    const seen = captureRequest();
    await fetchTransactionPage(
      { from: "2026-10-01", to: "2026-10-31", status: "Posted", limit: 10 },
      "a+b/c==",
    );
    const params = new URL(seen[0]!.url).searchParams;
    expect(Object.fromEntries(params)).toEqual({
      limit: "10",
      from: "2026-10-01",
      to: "2026-10-31",
      status: "Posted",
      cursor: "a+b/c==",
    });
  });

  it("carries the CSRF header like every other API call", async () => {
    const seen = captureRequest();
    await fetchTransactionPage({ limit: 25 }, undefined);
    expect(seen[0]!.headers.get("x-csrf")).toBe("1");
  });

  it("surfaces a problem response as ApiError with its code and traceId", async () => {
    server.use(
      http.get("/api/v1/transactions", () =>
        HttpResponse.json(
          { title: "Bad Request", code: "invalid_cursor", traceId: "t-1" },
          { status: 400, headers: { "Content-Type": "application/problem+json" } },
        ),
      ),
    );
    const error = await fetchTransactionPage({ limit: 25 }, "x").catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 400, code: "invalid_cursor", traceId: "t-1" });
  });
});

describe("transactionsInfiniteOptions", () => {
  it("is keyed by the filters, so a new filter set never reuses another's pages", () => {
    expect(transactionsKey({ limit: 25 })).not.toEqual(
      transactionsKey({ limit: 25, status: "Posted" }),
    );
    expect(transactionsKey({ limit: 25, status: "Posted" })).toEqual(
      transactionsKey({ limit: 25, status: "Posted" }),
    );
  });

  it("asks for the next page with the previous page's cursor, and stops at null", () => {
    const options = transactionsInfiniteOptions({ limit: 25 });
    const page = (nextCursor: string | null) => ({ items: [], nextCursor });
    expect(options.getNextPageParam(page("abc"), [], undefined, [])).toBe("abc");
    expect(options.getNextPageParam(page(null), [], undefined, [])).toBeUndefined();
    expect(options.initialPageParam).toBeUndefined();
  });
});

describe("flattenPages", () => {
  it("concatenates pages in order", () => {
    const [a, b, c, d] = makeTransactions(4) as [
      ReturnType<typeof makeTransaction>,
      ReturnType<typeof makeTransaction>,
      ReturnType<typeof makeTransaction>,
      ReturnType<typeof makeTransaction>,
    ];
    expect(
      flattenPages([
        { items: [a, b], nextCursor: "x" },
        { items: [c, d], nextCursor: null },
      ]),
    ).toEqual([a, b, c, d]);
  });

  it("keeps the first occurrence of a repeated id", () => {
    const original = makeTransaction({ merchantName: "First" }, 0);
    const repeated = makeTransaction({ merchantName: "Second" }, 0);
    const other = makeTransaction({}, 1);
    const flat = flattenPages([
      { items: [original, other], nextCursor: "x" },
      { items: [repeated], nextCursor: null },
    ]);
    expect(flat.map((item) => item.merchantName)).toEqual(["First", "Merchant 0002"]);
  });
});

// Checked by `npm run typecheck`: if the API adds or renames a status, or the filter list
// drifts from it, the build fails here instead of the filter silently missing a value.
describe("contract types", () => {
  it("keeps the filter's status list identical to the API's enum", () => {
    expectTypeOf<TransactionStatus>().toEqualTypeOf<components["schemas"]["TransactionStatus"]>();
    expectTypeOf<ReturnType<typeof makeTransaction>>().toEqualTypeOf<
      components["schemas"]["TransactionItem"]
    >();
  });
});
