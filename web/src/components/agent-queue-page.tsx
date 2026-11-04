import { useMemo } from "react";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { getRouteApi, Link } from "@tanstack/react-router";

import { agentQueueInfiniteOptions, type AgentQueueItem } from "@/api/agent";
import { disputeReasonsOptions, type DisputeReason } from "@/api/disputes";
import { DisputeStatusBadge } from "@/components/dispute-status-badge";
import { PagedResults } from "@/components/paged-results";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import {
  QUEUE_OPEN,
  QUEUE_STATUSES,
  QUEUE_STATUS_LABELS,
  type QueueFilters,
  type QueueStatus,
} from "@/lib/agent-filters";
import { reasonLabel } from "@/lib/dispute-reasons-catalogue";
import { formatDate, formatMoney } from "@/lib/format";

// getRouteApi avoids importing the route here (the route imports this component).
const route = getRouteApi("/agent");

const HEADER = "px-4 py-3 text-left font-medium text-muted-foreground";

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
        <h1 className="text-2xl font-semibold">Review queue</h1>
        <p className="text-sm text-muted-foreground">
          Disputes across customers, oldest first. Dates are shown in UTC.
        </p>
      </div>
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
      <QueueResults filters={filters} />
    </div>
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
      renderItems={(items) => <QueueTable items={items} reasons={reasons.data} />}
    />
  );
}

function QueueTable({
  items,
  reasons,
}: {
  items: readonly AgentQueueItem[];
  reasons: readonly DisputeReason[] | undefined;
}) {
  return (
    <div
      role="region"
      aria-label="Queue table"
      tabIndex={0}
      className="overflow-x-auto rounded-xl border"
    >
      <table className="w-full min-w-[48rem] border-collapse text-sm">
        <caption className="sr-only">Disputes awaiting review, oldest first</caption>
        <thead className="border-b bg-muted/50">
          <tr>
            <th scope="col" className={HEADER}>
              Raised (UTC)
            </th>
            <th scope="col" className={HEADER}>
              Customer
            </th>
            <th scope="col" className={HEADER}>
              Merchant
            </th>
            <th scope="col" className={`${HEADER} text-right`}>
              Amount
            </th>
            <th scope="col" className={HEADER}>
              Reason
            </th>
            <th scope="col" className={HEADER}>
              Status
            </th>
            <th scope="col" className={HEADER}>
              Details
            </th>
          </tr>
        </thead>
        <tbody className="divide-y">
          {items.map((item) => (
            <tr key={item.id} className="align-top">
              <td className="px-4 py-3 whitespace-nowrap">
                <time dateTime={item.createdAt}>{formatDate(item.createdAt)}</time>
              </td>
              <td className="px-4 py-3">
                {item.customerDisplayName}
                <span className="block text-xs text-muted-foreground">
                  {item.maskedAccountNumber}
                </span>
              </td>
              <th scope="row" className="px-4 py-3 text-left font-medium">
                {item.merchantName}
              </th>
              <td className="px-4 py-3 text-right whitespace-nowrap">
                {formatMoney(item.amount, item.currency)}
              </td>
              <td className="px-4 py-3">{reasonLabel(item.reasonCode, reasons)}</td>
              <td className="px-4 py-3">
                <DisputeStatusBadge status={item.status} />
              </td>
              <td className="px-4 py-3">
                <Link
                  to="/agent/disputes/$disputeId"
                  params={{ disputeId: item.id }}
                  className="underline underline-offset-4"
                >
                  Review
                  <span className="sr-only">
                    {" "}
                    dispute for {item.merchantName} from {item.customerDisplayName}
                  </span>
                </Link>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
