import { useMemo } from "react";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { getRouteApi, Link } from "@tanstack/react-router";

import { disputeReasonsOptions, disputesInfiniteOptions } from "@/api/disputes";
import { DisputesTable } from "@/components/disputes-table";
import { PagedResults } from "@/components/paged-results";
import {
  DISPUTE_STATUSES,
  DISPUTE_STATUS_LABELS,
  type DisputeFilters,
} from "@/lib/dispute-filters";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import type { DisputeStatus } from "@/api/disputes";
import { searchWithStatus } from "@/lib/status-links";
import { CARD, FILTER_LINK, LINK, PAGE_TITLE } from "@/lib/table-styles";

// getRouteApi avoids importing the route here (the route imports this component).
const route = getRouteApi("/disputes");

export function DisputesPage() {
  const { status, limit } = route.useSearch();
  const navigate = route.useNavigate();
  const filters = useMemo<DisputeFilters>(() => ({ status, limit }), [status, limit]);

  function chooseStatus(value: string) {
    const next = (DISPUTE_STATUSES as readonly string[]).includes(value)
      ? (value as DisputeStatus)
      : undefined;
    void navigate({ search: { status: next, limit } });
  }

  return (
    <div className="flex flex-col gap-6">
      <title>My disputes - Transactions Dispute Portal</title>
      <div className="flex flex-col gap-1.5">
        <h1 className={PAGE_TITLE}>My disputes</h1>
        <p className="text-muted-foreground">
          Every dispute you have raised, newest first. Dates are shown in UTC.
        </p>
      </div>
      <div className={CARD}>
        <div className="flex min-w-40 flex-col gap-1.5 sm:w-52">
          <Label htmlFor="dispute-status">Status</Label>
          <Select
            id="dispute-status"
            value={status ?? ""}
            onChange={(event) => chooseStatus(event.target.value)}
          >
            <option value="">All statuses</option>
            {DISPUTE_STATUSES.map((value) => (
              <option key={value} value={value}>
                {DISPUTE_STATUS_LABELS[value]}
              </option>
            ))}
          </Select>
        </div>
        <StatusLinks search={filters} />
        <DisputeResults filters={filters} />
      </div>
    </div>
  );
}

const STATUS_LINKS: readonly { label: string; value: DisputeStatus | undefined }[] = [
  { label: "All", value: undefined },
  ...DISPUTE_STATUSES.map((value) => ({ label: DISPUTE_STATUS_LABELS[value], value })),
];

/**
 * The URL is the only state, so the view is shareable and Back/Forward work. A status is one
 * choice from a fixed list, so it applies as soon as it is chosen. Links, not tabs.
 */
function StatusLinks({ search }: { search: DisputeFilters }) {
  return (
    <nav aria-label="Filter by status" className="flex flex-wrap gap-1.5">
      {STATUS_LINKS.map(({ label, value }) => (
        <Link
          key={label}
          to="/disputes"
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

function DisputeResults({ filters }: { filters: DisputeFilters }) {
  const options = useMemo(() => disputesInfiniteOptions(filters), [filters]);
  const query = useInfiniteQuery(options);
  // Labels only: if the catalogue is unavailable the table falls back to the raw codes.
  const reasons = useQuery(disputeReasonsOptions);

  return (
    <PagedResults
      query={query}
      queryKey={options.queryKey}
      filtersKey={JSON.stringify(filters)}
      label="Dispute list"
      noun={{ one: "dispute", many: "disputes" }}
      titles={{
        firstLoadFailed: "We could not load your disputes",
        nextPageFailed: "We could not load more disputes",
      }}
      empty={
        <div className="rounded-xl bg-accent p-8 text-center">
          {filters.status ? (
            <>
              <p className="font-medium">
                No {DISPUTE_STATUS_LABELS[filters.status].toLowerCase()} disputes
              </p>
              <p className="mt-1 text-sm text-muted-foreground">
                Choose another status, or all statuses.
              </p>
            </>
          ) : (
            <>
              <p className="font-medium">You have not raised any disputes yet</p>
              <p className="mt-1 text-sm text-muted-foreground">
                Find a transaction in{" "}
                <Link to="/transactions" className={LINK}>
                  your transactions
                </Link>{" "}
                to dispute it.
              </p>
            </>
          )}
        </div>
      }
      renderItems={(items) => <DisputesTable items={items} reasons={reasons.data} />}
    />
  );
}
