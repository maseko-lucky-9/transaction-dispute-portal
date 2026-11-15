import { useMemo } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { getRouteApi, Link } from "@tanstack/react-router";

import { transactionsInfiniteOptions } from "@/api/transactions";
import { PagedResults } from "@/components/paged-results";
import { TransactionFiltersForm, type AppliedFilters } from "@/components/transaction-filters-form";
import { TransactionsTable } from "@/components/transactions-table";
import { searchWithStatus } from "@/lib/status-links";
import { CARD, FILTER_LINK, PAGE_TITLE } from "@/lib/table-styles";
import {
  TRANSACTION_STATUS_LABELS,
  TRANSACTION_STATUSES,
  type TransactionFilters,
  type TransactionStatus,
} from "@/lib/transaction-filters";

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
      <div className="flex flex-col gap-1.5">
        <h1 className={PAGE_TITLE}>Transactions</h1>
        <p className="text-muted-foreground">Newest first. Dates are shown in UTC.</p>
      </div>
      <div className={CARD}>
        <TransactionFiltersForm
          applied={{ from, to, status }}
          onApply={(next) => void writeFilters(next)}
          onClear={() => void writeFilters({})}
        />
        <StatusLinks search={{ from, to, status, limit }} />
        <TransactionResults filters={filters} />
      </div>
    </div>
  );
}

const STATUS_LINKS: readonly { label: string; value: TransactionStatus | undefined }[] = [
  { label: "All", value: undefined },
  ...TRANSACTION_STATUSES.map((value) => ({ label: TRANSACTION_STATUS_LABELS[value], value })),
];

/** Links, not tabs: each is a navigation on the `status` param, keeping the other filters. */
function StatusLinks({ search }: { search: TransactionFilters }) {
  return (
    <nav aria-label="Filter by status" className="flex flex-wrap gap-1.5">
      {STATUS_LINKS.map(({ label, value }) => (
        <Link
          key={label}
          to="/transactions"
          search={searchWithStatus(search, value)}
          activeOptions={{ exact: true }}
          className={FILTER_LINK}
        >
          {label}
        </Link>
      ))}
    </nav>
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
