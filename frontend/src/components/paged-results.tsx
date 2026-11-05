import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import {
  useQueryClient,
  type InfiniteData,
  type QueryKey,
  type UseInfiniteQueryResult,
} from "@tanstack/react-query";

import { isApiError } from "@/api/problem";
import { flattenPages, type Page } from "@/api/paging";
import { ErrorState } from "@/components/error-state";
import { ListSkeleton } from "@/components/list-skeleton";
import { Button } from "@/components/ui/button";

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

interface PagedResultsProps<T extends { id: string }> {
  /** The result of `useInfiniteQuery(options)` for a keyset-paged list. */
  query: UseInfiniteQueryResult<InfiniteData<Page<T>>>;
  /** Must be referentially stable between renders: memoize the options it comes from. */
  queryKey: QueryKey;
  /** Identifies the filter set; the one-shot cursor recovery starts afresh when it changes. */
  filtersKey: string;
  /** Accessible name of the list section. */
  label: string;
  /** What the list holds, for announcements: "transaction" / "transactions". */
  noun: { one: string; many: string };
  titles: { firstLoadFailed: string; nextPageFailed: string };
  /** Shown instead of the list when there are no items. */
  empty: ReactNode;
  renderItems: (items: T[]) => ReactNode;
}

/**
 * A keyset-paged list with everything a keyboard and screen-reader user needs:
 * loading, empty and error states, Load more, and ONE persistent result summary (a live
 * region). It is deliberately NOT keyed by the filters by the caller: the summary stays the
 * same element, so a screen reader hears the new result after a filter changes instead of
 * meeting a freshly created, silent region.
 */
export function PagedResults<T extends { id: string }>({
  query,
  queryKey,
  filtersKey,
  label,
  noun,
  titles,
  empty,
  renderItems,
}: PagedResultsProps<T>) {
  const queryClient = useQueryClient();
  const { data, error, isPending, isFetchingNextPage, hasNextPage, fetchNextPage, refetch } = query;
  const summaryRef = useRef<HTMLParagraphElement>(null);

  // `queryKey` must be referentially stable (memoize the options in the caller).
  const startOver = useCallback(
    () => queryClient.resetQueries({ queryKey }),
    [queryClient, queryKey],
  );

  const items = useMemo(() => (data ? flattenPages(data.pages) : []), [data]);
  const count = items.length;

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
    if (queryClient.getQueryState(queryKey)?.status === "success") {
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
        title={titles.firstLoadFailed}
        onRetry={() => void retryFirstLoad()}
      />
    );
  } else if (count === 0) {
    body = empty;
  } else {
    body = renderItems(items);
  }

  let summary = "";
  if (isPending) summary = `Loading ${noun.many}`;
  else if (data && isFetchingNextPage) summary = `Loading more ${noun.many}`;
  else if (data) {
    summary = `Showing ${count} ${count === 1 ? noun.one : noun.many}${hasNextPage ? "" : ", all loaded"}`;
  }

  return (
    <section aria-label={label} className="flex flex-col gap-4">
      <CursorRecovery key={filtersKey} error={error} onRestart={startOver} />
      {body}
      {data && error ? (
        <ErrorState
          error={error}
          title={titles.nextPageFailed}
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
