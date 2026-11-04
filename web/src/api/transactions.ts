import { infiniteQueryOptions } from "@tanstack/react-query";

import type { ListTransactionsQuery, TransactionFilters } from "@/lib/transaction-filters";

import { apiFetch } from "./client";
import type { components } from "./schema";

/** Generated from `docs/openapi.json` by `npm run gen:types`; never edit by hand. */
export type TransactionItem = components["schemas"]["TransactionItem"];
export type TransactionPage = components["schemas"]["TransactionListResponse"];

export const transactionsKey = (filters: TransactionFilters) =>
  ["transactions", "list", filters] as const;

/** `GET /api/v1/transactions`. Absent filters are not sent at all. */
export function fetchTransactionPage(
  filters: TransactionFilters,
  cursor: string | undefined,
  signal?: AbortSignal,
): Promise<TransactionPage> {
  // `satisfies` ties every name and value to the generated operation: a renamed parameter or a
  // status the API does not have stops compiling.
  const query = {
    limit: filters.limit,
    from: filters.from,
    to: filters.to,
    status: filters.status,
    cursor,
  } satisfies ListTransactionsQuery;

  const params = new URLSearchParams();
  for (const [name, value] of Object.entries(query)) {
    if (value !== undefined && value !== "") params.set(name, String(value));
  }

  return apiFetch<TransactionPage>(
    `/api/v1/transactions?${params.toString()}`,
    signal ? { signal } : {},
  );
}

/** Keyset paging: each page's `nextCursor` is the next request's `cursor`. */
export function transactionsInfiniteOptions(filters: TransactionFilters) {
  return infiniteQueryOptions({
    queryKey: transactionsKey(filters),
    queryFn: ({ pageParam, signal }) => fetchTransactionPage(filters, pageParam, signal),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
    // Refetching an infinite query replays every loaded page; do it on demand, not on focus.
    refetchOnWindowFocus: false,
    staleTime: 60_000,
  });
}

/** Flatten pages into one list. First occurrence wins, so a repeated row is never rendered twice. */
export function flattenPages(pages: readonly TransactionPage[]): TransactionItem[] {
  const seen = new Set<string>();
  const items: TransactionItem[] = [];
  for (const page of pages) {
    for (const item of page.items) {
      if (seen.has(item.id)) continue;
      seen.add(item.id);
      items.push(item);
    }
  }
  return items;
}
