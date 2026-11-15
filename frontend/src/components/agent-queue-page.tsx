import { useMemo } from "react";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { getRouteApi, Link } from "@tanstack/react-router";

import { agentQueueInfiniteOptions, type AgentQueueItem } from "@/api/agent";
import { disputeReasonsOptions, type DisputeReason, type DisputeStatus } from "@/api/disputes";
import { DisputeStatusPill } from "@/components/status-pill";
import { TableCard } from "@/components/table-card";
import { PagedResults } from "@/components/paged-results";
import { LifecycleStepper } from "@/components/lifecycle-stepper";
import { buttonVariants } from "@/components/ui/button";
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
import { reasonLabel } from "@/lib/dispute-reasons-catalogue";
import { formatDate, formatMoney } from "@/lib/format";
import { searchWithStatus } from "@/lib/status-links";
import {
  AMOUNT_CELL,
  CARD,
  COLUMN_HEADER,
  FILTER_LINK,
  PAGE_TITLE,
  TABLE_ROW,
} from "@/lib/table-styles";

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
      renderItems={(items) => <QueueTable items={items} reasons={reasons.data} />}
    />
  );
}

/** Submitted is the call to act; Under review is in hand; a decided row is only for reading. */
const REVIEW_VARIANT: Record<DisputeStatus, "default" | "outline" | "ghost"> = {
  Submitted: "default",
  UnderReview: "outline",
  Accepted: "ghost",
  Rejected: "ghost",
  Withdrawn: "ghost",
};

function QueueTable({
  items,
  reasons,
}: {
  items: readonly AgentQueueItem[];
  reasons: readonly DisputeReason[] | undefined;
}) {
  return (
    <TableCard label="Queue table" flat>
      <table className="w-full min-w-[48rem] border-collapse text-sm">
        <caption className="sr-only">Disputes awaiting review, oldest first</caption>
        <thead>
          <tr>
            <th scope="col" className={COLUMN_HEADER}>
              Raised (UTC)
            </th>
            <th scope="col" className={COLUMN_HEADER}>
              Customer
            </th>
            <th scope="col" className={COLUMN_HEADER}>
              Merchant
            </th>
            <th scope="col" className={`${COLUMN_HEADER} text-right`}>
              Amount
            </th>
            <th scope="col" className={COLUMN_HEADER}>
              Reason
            </th>
            <th scope="col" className={COLUMN_HEADER}>
              Progress
            </th>
            <th scope="col" className={COLUMN_HEADER}>
              Status
            </th>
            <th scope="col" className={COLUMN_HEADER}>
              Details
            </th>
          </tr>
        </thead>
        <tbody className="divide-y">
          {items.map((item) => (
            <tr key={item.id} className={TABLE_ROW}>
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
              <td className={AMOUNT_CELL}>{formatMoney(item.amount, item.currency)}</td>
              <td className="px-4 py-3">{reasonLabel(item.reasonCode, reasons)}</td>
              <td className="px-4 py-3">
                <LifecycleStepper status={item.status} />
              </td>
              <td className="px-4 py-3">
                <DisputeStatusPill status={item.status} />
              </td>
              <td className="px-4 py-3">
                <Link
                  to="/agent/disputes/$disputeId"
                  params={{ disputeId: item.id }}
                  className={buttonVariants({
                    variant: REVIEW_VARIANT[item.status],
                    size: "sm",
                  })}
                >
                  {/* The space sits outside the sr-only span: a flex item drops its leading space. */}
                  Review{" "}
                  <span className="sr-only">
                    dispute for {item.merchantName} from {item.customerDisplayName}
                  </span>
                </Link>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </TableCard>
  );
}
