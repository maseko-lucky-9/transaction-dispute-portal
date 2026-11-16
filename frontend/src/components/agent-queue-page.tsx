import { useMemo } from "react";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { getRouteApi, Link } from "@tanstack/react-router";

import { agentQueueInfiniteOptions } from "@/api/agent";
import { disputeReasonsOptions } from "@/api/disputes";
import { PagedResults } from "@/components/paged-results";
import { QueueTable } from "@/components/queue-table";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import {
  QUEUE_OPEN,
  QUEUE_STATUSES,
  QUEUE_STATUS_LABELS,
  type QueueFilters,
  type QueueStatus,
} from "@/lib/agent-filters";
import { DISPUTE_STATUSES, DISPUTE_STATUS_LABELS } from "@/lib/dispute-filters";
import { searchWithStatus } from "@/lib/status-links";
import { CARD, FILTER_LINK, PAGE_TITLE } from "@/lib/table-styles";

// getRouteApi avoids importing the route here (the route imports this component).
const route = getRouteApi("/agent");

export function AgentQueuePage() {
  const { status, limit } = route.useSearch();
  const navigate = route.useNavigate();
  const filters = useMemo<QueueFilters>(() => ({ status, limit }), [status, limit]);

  function chooseStatus(value: string) {
    const next = (QUEUE_STATUSES as readonly string[]).includes(value)
      ? (value as QueueStatus)
      : QUEUE_OPEN;
    void navigate({ search: { status: next, limit } });
  }

  return (
    <div className="flex flex-col gap-6">
      <title>Review queue - Transactions Dispute Portal</title>
      <div className="flex flex-col gap-1">
        <h1 className={PAGE_TITLE}>Review queue</h1>
        <p className="text-sm text-muted-foreground">
          Disputes across customers, oldest first. Dates are shown in UTC.
        </p>
      </div>
      <div className={CARD}>
        <div className="flex w-64 flex-col gap-1.5">
          <Label htmlFor="queue-status">Status</Label>
          <Select
            id="queue-status"
            value={status}
            onChange={(event) => chooseStatus(event.target.value)}
          >
            {QUEUE_STATUSES.map((value) => (
              <option key={value} value={value}>
                {QUEUE_STATUS_LABELS[value]}
              </option>
            ))}
          </Select>
        </div>
        <StatusLinks search={filters} />
        <QueueResults filters={filters} />
      </div>
    </div>
  );
}

const STATUS_LINKS: readonly { label: string; value: QueueStatus }[] = [
  { label: "Open", value: QUEUE_OPEN },
  ...DISPUTE_STATUSES.map((value) => ({ label: DISPUTE_STATUS_LABELS[value], value })),
];

/**
 * Links, not tabs, on the same `status` param as the select. Open is the default, so its link
 * carries no `status` (the URL stays clean, as when the select returns to Open).
 */
function StatusLinks({ search }: { search: QueueFilters }) {
  return (
    <nav aria-label="Filter by status" className="flex flex-wrap gap-1.5">
      {STATUS_LINKS.map(({ label, value }) => (
        <Link
          key={label}
          to="/agent"
          search={searchWithStatus(search, value === QUEUE_OPEN ? undefined : value)}
          activeOptions={{ exact: true }}
          className={FILTER_LINK}
        >
          {label}
        </Link>
      ))}
    </nav>
  );
}

function QueueResults({ filters }: { filters: QueueFilters }) {
  const options = useMemo(() => agentQueueInfiniteOptions(filters), [filters]);
  const query = useInfiniteQuery(options);
  const reasons = useQuery(disputeReasonsOptions);

  return (
    <PagedResults
      query={query}
      queryKey={options.queryKey}
      filtersKey={JSON.stringify(filters)}
      label="Review queue list"
      noun={{ one: "dispute", many: "disputes" }}
      titles={{
        firstLoadFailed: "We could not load the review queue",
        nextPageFailed: "We could not load more disputes",
      }}
      empty={
        <div className="rounded-xl border p-8 text-center">
          <p className="font-medium">
            {filters.status === QUEUE_OPEN
              ? "Nothing is waiting for a decision"
              : `No ${QUEUE_STATUS_LABELS[filters.status].toLowerCase()} disputes`}
          </p>
        </div>
      }
      renderItems={(items) => (
        <QueueTable
          items={items}
          reasons={reasons.data}
          label="Queue table"
          caption="Disputes awaiting review, oldest first"
        />
      )}
    />
  );
}
