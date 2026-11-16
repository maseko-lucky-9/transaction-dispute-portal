import { useMemo } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";

import { transactionsInfiniteOptions } from "@/api/transactions";
import { PagedResults } from "@/components/paged-results";
import { TransactionsTable } from "@/components/transactions-table";
import type { TransactionFilters } from "@/lib/transaction-filters";

/** One page of transactions, with loading, empty, error and Load more. Used by Home and /transactions. */
export function TransactionResults({ filters }: { filters: TransactionFilters }) {
  const options = useMemo(() => transactionsInfiniteOptions(filters), [filters]);
  const query = useInfiniteQuery(options);
  const hasFilters = Boolean(filters.from || filters.to || filters.status);

  return (
    <PagedResults
      query={query}
      queryKey={options.queryKey}
      filtersKey={JSON.stringify(filters)}
      label="Transaction list"
      noun={{ one: "transaction", many: "transactions" }}
      titles={{
        firstLoadFailed: "We could not load your transactions",
        nextPageFailed: "We could not load more transactions",
      }}
      empty={
        <div className="rounded-xl bg-accent p-8 text-center">
          <p className="font-medium">
            {hasFilters ? "No transactions match these filters" : "You have no transactions yet"}
          </p>
          {hasFilters ? (
            <p className="mt-1 text-sm text-muted-foreground">
              Try a wider date range, or clear the filters.
            </p>
          ) : null}
        </div>
      }
      renderItems={(items) => <TransactionsTable items={items} />}
    />
  );
}
