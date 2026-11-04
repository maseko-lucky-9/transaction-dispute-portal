import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
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
      <TransactionResults filters={filters} />
    </div>
  );
}

function isInvalidCursor(error: unknown): boolean {
  return isApiError(error) && error.status === 400 && error.code === "invalid_cursor";
}

/** Focus a node that has just replaced the focused one, but never steal focus from the user. */
function focusIfFocusWasLost(element: HTMLElement | null): void {
  if (element && (document.activeElement === null || document.activeElement === document.body)) {
    element.focus();
  }
}

/**
 * An expired or foreign cursor cannot be fixed by retrying the same page, so start again from
 * the first page, ONCE, and say so. A second failure is left to the error state. Keyed by the
 * filters by the caller, so every filter set gets a fresh one-shot.
 */
function CursorRecovery({ error, onRestart }: { error: unknown; onRestart: () => Promise<void> }) {
  const [reloaded, setReloaded] = useState(false);
  const used = useRef(false);

  useEffect(() => {
    if (!isInvalidCursor(error) || used.current) return;
    used.current = true;
    void onRestart().then(() => setReloaded(true));
  }, [error, onRestart]);

  if (!reloaded) return null;
  return (
    // The reload replaced the list (and the Load more button that had focus): land here.
    <p
      role="status"
      tabIndex={-1}
      ref={focusIfFocusWasLost}
      className="rounded-md border bg-muted/50 px-4 py-2 text-sm outline-none"
    >
      Your list was out of date, so we reloaded it from the start.
    </p>
  );
}

/**
 * The list, its Load more button and the result summary. Deliberately NOT keyed by the
 * filters: the summary is one live region for the whole page, so a screen reader hears the
 * new result after "Apply filters" instead of meeting a freshly created, silent region.
 */
function TransactionResults({ filters }: { filters: TransactionFilters }) {
  const queryClient = useQueryClient();
  const options = useMemo(() => transactionsInfiniteOptions(filters), [filters]);
  const query = useInfiniteQuery(options);
  const { data, error, isPending, isFetchingNextPage, hasNextPage, fetchNextPage, refetch } = query;
  const summaryRef = useRef<HTMLParagraphElement>(null);

  const startOver = useCallback(
    () => queryClient.resetQueries({ queryKey: options.queryKey }),
    [queryClient, options.queryKey],
  );

  const items = useMemo(() => (data ? flattenPages(data.pages) : []), [data]);
  const count = items.length;
  const hasFilters = Boolean(filters.from || filters.to || filters.status);

  async function loadMore() {
    if (isFetchingNextPage) return; // aria-disabled, unlike `disabled`, still takes clicks
    const result = await fetchNextPage();
    // The button disappears with the last page; do not drop keyboard focus on <body>.
    if (result.isSuccess && !result.hasNextPage) summaryRef.current?.focus();
  }

  /**
   * "Try again" disappears when it works, taking keyboard focus with it. Land on the summary,
   * which also announces the outcome.
   */
  async function recover(action: () => Promise<unknown>) {
    await action();
    if (queryClient.getQueryState(options.queryKey)?.status === "success") {
      summaryRef.current?.focus();
    }
  }

  const retryFirstLoad = () => recover(() => (isInvalidCursor(error) ? startOver() : refetch()));
  const retryNextPage = () =>
    recover(() =>
      isInvalidCursor(error) ? startOver() : hasNextPage ? fetchNextPage() : refetch(),
    );

  let body: ReactNode;
  if (isPending) {
    body = <ListSkeleton announce={false} />;
  } else if (!data) {
    body = (
      <ErrorState
        error={error}
        title="We could not load your transactions"
        onRetry={() => void retryFirstLoad()}
      />
    );
  } else if (count === 0) {
    body = (
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
    );
  } else {
    body = <TransactionsTable items={items} />;
  }

  let summary = "";
  if (isPending) summary = "Loading transactions";
  else if (data && isFetchingNextPage) summary = "Loading more transactions";
  else if (data) {
    const noun = count === 1 ? "transaction" : "transactions";
    summary = `Showing ${count} ${noun}${hasNextPage ? "" : ", all loaded"}`;
  }

  return (
    <section aria-label="Transaction list" className="flex flex-col gap-4">
      <CursorRecovery key={JSON.stringify(filters)} error={error} onRestart={startOver} />
      {body}
      {data && error ? (
        <ErrorState
          error={error}
          title="We could not load more transactions"
          onRetry={() => void retryNextPage()}
        />
      ) : null}
      <div className="flex items-center gap-4">
        {/* Stays mounted through a failure (and while loading, via aria-disabled) so a keyboard user keeps focus. */}
        {hasNextPage ? (
          <Button
            variant="outline"
            aria-disabled={isFetchingNextPage ? true : undefined}
            onClick={() => void loadMore()}
          >
            {isFetchingNextPage ? "Loading..." : "Load more"}
          </Button>
        ) : null}
        <p
          ref={summaryRef}
          role="status"
          aria-label="Result count"
          tabIndex={-1}
          className="text-sm text-muted-foreground outline-none"
        >
          {summary}
        </p>
      </div>
    </section>
  );
}
