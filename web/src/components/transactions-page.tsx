import { useMemo } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { getRouteApi } from "@tanstack/react-router";

import { transactionsInfiniteOptions } from "@/api/transactions";
import { PagedResults } from "@/components/paged-results";
import { TransactionFiltersForm, type AppliedFilters } from "@/components/transaction-filters-form";
import { TransactionsTable } from "@/components/transactions-table";
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

function TransactionResults({ filters }: { filters: TransactionFilters }) {
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
      }
      renderItems={(items) => <TransactionsTable items={items} />}
    />
  );
}
