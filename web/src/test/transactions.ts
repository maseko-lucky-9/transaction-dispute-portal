import { http, HttpResponse } from "msw";

import type { TransactionItem } from "@/api/transactions";
import { dateRangeProblem, isIsoDate, TRANSACTION_STATUSES } from "@/lib/transaction-filters";

import { problem } from "./handlers";

const DAY_MS = 86_400_000;
const NEWEST = Date.UTC(2026, 10, 3, 10, 0, 0); // 2026-11-03T10:00:00Z

/** A posted, disputable debit unless overridden. `index` keeps ids and references unique. */
export function makeTransaction(
  overrides: Partial<TransactionItem> = {},
  index = 0,
): TransactionItem {
  const n = String(index + 1).padStart(4, "0");
  return {
    id: `00000000-0000-4000-8000-${String(index + 1).padStart(12, "0")}`,
    reference: `REF-${n}`,
    merchantName: `Merchant ${n}`,
    category: "Groceries",
    amount: "123.45",
    currency: "ZAR",
    direction: "Debit",
    status: "Posted",
    transactionDate: new Date(NEWEST - index * DAY_MS).toISOString(),
    postedAt: new Date(NEWEST - index * DAY_MS + 3_600_000).toISOString(),
    account: { maskedNumber: "**** 1234" },
    isDisputable: true,
    disputeBlockReasons: [],
    dispute: null,
    ...overrides,
  };
}

/** `count` transactions, newest first, one per day going back from 2026-11-03. */
export function makeTransactions(count: number): TransactionItem[] {
  return Array.from({ length: count }, (_, index) => makeTransaction({}, index));
}

export interface FakeApi {
  /** Every request the handler saw, in order. */
  requests: URL[];
}

const encodeCursor = (offset: number) => btoa(`cursor:${offset}`);
const decodeCursor = (cursor: string): number | null => {
  try {
    const match = /^cursor:(\d+)$/.exec(atob(cursor));
    return match ? Number(match[1]) : null;
  } catch {
    return null;
  }
};

/**
 * Stands in for GET /api/v1/transactions: keyset-style opaque cursor, the same filters and the
 * same 400 rules as the server. A client that sends a bad query therefore fails the test the
 * way it would fail in production, instead of being quietly accepted by a lenient fake.
 */
export function fakeTransactionsApi(
  all: TransactionItem[],
  /** Return a response to short-circuit the fake (e.g. a 5xx); undefined falls through. */
  intercept?: (url: URL) => Response | undefined,
): {
  api: FakeApi;
  handler: ReturnType<typeof http.get>;
} {
  const api: FakeApi = { requests: [] };

  const handler = http.get("/api/v1/transactions", ({ request }) => {
    const url = new URL(request.url);
    api.requests.push(url);
    const intercepted = intercept?.(url);
    if (intercepted) return intercepted;
    const q = url.searchParams;

    const errors: Record<string, string[]> = {};
    const limit = Number(q.get("limit") ?? 25);
    if (!Number.isInteger(limit) || limit < 1 || limit > 100)
      errors["limit"] = ["limit must be 1..100"];
    const from = q.get("from");
    const to = q.get("to");
    if (from && !isIsoDate(from)) errors["from"] = ["from must be a date"];
    if (to && !isIsoDate(to)) errors["to"] = ["to must be a date"];
    if (from && to && !errors["from"] && !errors["to"] && dateRangeProblem(from, to)) {
      errors["to"] = ["range must be from <= to and at most 366 days"];
    }
    const status = q.get("status");
    if (status && !(TRANSACTION_STATUSES as readonly string[]).includes(status)) {
      errors["status"] = ["unknown status"];
    }
    if (Object.keys(errors).length > 0) {
      return problem(400, {
        title: "One or more validation errors occurred.",
        code: "validation_failed",
        errors,
      });
    }

    let offset = 0;
    const cursor = q.get("cursor");
    if (cursor) {
      const decoded = decodeCursor(cursor);
      if (decoded === null) {
        return problem(400, {
          title: "Bad Request",
          detail: "The cursor is not valid.",
          code: "invalid_cursor",
        });
      }
      offset = decoded;
    }

    const matching = all.filter((item) => {
      if (status && item.status !== status) return false;
      const day = item.transactionDate.slice(0, 10);
      if (from && day < from) return false;
      if (to && day > to) return false;
      return true;
    });
    const items = matching.slice(offset, offset + limit);
    const next = offset + limit;
    return HttpResponse.json({
      items,
      nextCursor: next < matching.length ? encodeCursor(next) : null,
    });
  });

  return { api, handler };
}
