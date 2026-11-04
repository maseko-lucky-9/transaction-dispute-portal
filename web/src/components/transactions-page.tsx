import { useEffect, useMemo, useRef, useState } from "react";
import { useInfiniteQuery, useQueryClient } from "@tanstack/react-query";
import { getRouteApi } from "@tanstack/react-router";

import { isApiError } from "@/api/problem";
import { flattenPages, transactionsInfiniteOptions } from "@/api/transactions";
import { ErrorState } from "@/components/error-state";
import { ListSkeleton } from "@/components/list-skeleton";
import { TransactionFiltersForm, type AppliedFilters } from "@/components/transaction-filters-form";
import { TransactionsTable } from "@/components/transactions-table";
import { Button } from "@/components/ui/button";
import type { TransactionFilters } from "@/lib/transaction-filters";

// getRouteApi avoids importing the route here (the route imports this component).
const route = getRouteApi("/transactions");

export function TransactionsPage() {
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const { from, to, status, limit } = search;

  const filters = useMemo<TransactionFilters>(
    () => ({ from, to, status, limit }),
    [from, to, status, limit],
  );

  // The URL is the only state: applying a filter is a navigation, so the view is shareable
  // and Back/Forward work. The route strips `limit` from the URL when it is the default.
  const writeFilters = (next: AppliedFilters) => navigate({ search: { ...next, limit } });

  return (
    <div className="flex flex-col gap-6">
      <title>Transactions - Transactions Dispute Portal</title>
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold">Transactions</h1>
        <p className="text-sm text-muted-foreground">Newest first. Dates are shown in UTC.</p>
      </div>
      <TransactionFiltersForm
        applied={{ from, to, status }}
        onApply={(next) => void writeFilters(next)}
        onClear={() => void writeFilters({})}
      />
      {/* Keyed by the filters: a new filter set is a fresh list with fresh one-shot state. */}
      <TransactionList key={JSON.stringify(filters)} filters={filters} />
    </div>
  );
}

function isInvalidCursor(error: unknown): boolean {
  return isApiError(error) && error.status === 400 && error.code === "invalid_cursor";
}

function TransactionList({ filters }: { filters: TransactionFilters }) {
  const queryClient = useQueryClient();
  const options = useMemo(() => transactionsInfiniteOptions(filters), [filters]);
  const query = useInfiniteQuery(options);
  const { data, error, isPending, isFetchingNextPage, hasNextPage, fetchNextPage, refetch } = query;

  const [reloadedFromStart, setReloadedFromStart] = useState(false);
  const autoResetUsed = useRef(false);
  const statusRef = useRef<HTMLParagraphElement>(null);

  const startOver = () => queryClient.resetQueries({ queryKey: options.queryKey });

  // An expired or foreign cursor cannot be fixed by retrying the same page. Start again from
  // the first page, once, and say so; a second failure falls through to the error state.
  useEffect(() => {
    if (!isInvalidCursor(error) || autoResetUsed.current) return;
    autoResetUsed.current = true;
    void startOver().then(() => setReloadedFromStart(true));
    // startOver only closes over stable values (queryClient, options).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [error]);

  const items = useMemo(() => (data ? flattenPages(data.pages) : []), [data]);

  async function loadMore() {
    const result = await fetchNextPage();
    // The button disappears with the last page; do not drop keyboard focus on <body>.
    if (result.isSuccess && !result.hasNextPage) statusRef.current?.focus();
  }

  if (isPending && !error) return <ListSkeleton />;
  if (!data) {
    return (
      <ErrorState
        error={error}
        title="We could not load your transactions"
        onRetry={() => void (isInvalidCursor(error) ? startOver() : refetch())}
      />
    );
  }

  const hasFilters = Boolean(filters.from || filters.to || filters.status);
  const count = items.length;

  return (
    <section aria-label="Transaction list" className="flex flex-col gap-4">
      {reloadedFromStart ? (
        <p role="status" className="rounded-md border bg-muted/50 px-4 py-2 text-sm">
          Your list was out of date, so we reloaded it from the start.
        </p>
      ) : null}

      {count === 0 ? (
        <div className="rounded-xl border p-8 text-center">
          <p className="font-medium">
            {hasFilters ? "No transactions match these filters" : "You have no transactions yet"}
          </p>
          {hasFilters ? (
            <p className="mt-1 text-sm text-muted-foreground">
              Try a wider date range, or clear the filters.
            </p>
          ) : null}
        </div>
      ) : (
        <TransactionsTable items={items} />
      )}

      {error ? (
        <ErrorState
          error={error}
          title="We could not load more transactions"
          onRetry={() =>
            void (isInvalidCursor(error) ? startOver() : hasNextPage ? fetchNextPage() : refetch())
          }
        />
      ) : null}

      <div className="flex items-center gap-4">
        {hasNextPage && !error ? (
          <Button variant="outline" onClick={() => void loadMore()} disabled={isFetchingNextPage}>
            {isFetchingNextPage ? "Loading..." : "Load more"}
          </Button>
        ) : null}
        {/* A live region: the new count is announced after each page. */}
        <p
          ref={statusRef}
          role="status"
          tabIndex={-1}
          className="text-sm text-muted-foreground outline-none"
        >
          {count === 0
            ? ""
            : isFetchingNextPage
              ? "Loading more transactions"
              : `Showing ${count} ${count === 1 ? "transaction" : "transactions"}${hasNextPage ? "" : ", all loaded"}`}
        </p>
      </div>
    </section>
  );
}
