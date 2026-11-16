import { Link } from "@tanstack/react-router";

import type { AgentQueueItem } from "@/api/agent";
import type { DisputeReason, DisputeStatus } from "@/api/disputes";
import { LifecycleStepper } from "@/components/lifecycle-stepper";
import { DisputeStatusPill } from "@/components/status-pill";
import { TableCard } from "@/components/table-card";
import { buttonVariants } from "@/components/ui/button";
import { assignmentOf, claimAge, reviewedByAnother } from "@/lib/assignment";
import { reasonLabel } from "@/lib/dispute-reasons-catalogue";
import { formatDate, formatMoney } from "@/lib/format";
import { AMOUNT_CELL, COLUMN_HEADER, TABLE_ROW } from "@/lib/table-styles";

/**
 * An open row is outlined and a decided one is only for reading. Never crimson: a page has one
 * crimson action (Take next on Home), and a list of rows is not it.
 */
const LINK_VARIANT: Record<DisputeStatus, "outline" | "ghost"> = {
  Submitted: "outline",
  UnderReview: "outline",
  Accepted: "ghost",
  Rejected: "ghost",
  Withdrawn: "ghost",
};

interface QueueTableProps {
  items: readonly AgentQueueItem[];
  reasons: readonly DisputeReason[] | undefined;
  /** Accessible name of the scrollable region; unique on a page that has several tables. */
  label: string;
  /** Screen-reader caption: what the rows are. */
  caption: string;
  /**
   * "review" opens the dispute to act on it; "read" for disputes someone else is reviewing, which
   * this agent may read but not decide. A row nobody is named on (a legacy review with no
   * assignee, which any agent may decide) keeps "Review" even then.
   */
  action?: "review" | "read";
  /** Add a column with how long ago the dispute was claimed. */
  showClaimed?: boolean;
}

/** The queue's rows: when raised, who, what, why, how far along, and a link to the dispute. */
export function QueueTable({
  items,
  reasons,
  label,
  caption,
  action = "review",
  showClaimed = false,
}: QueueTableProps) {
  return (
    <TableCard label={label} flat>
      <table className="w-full min-w-[48rem] border-collapse text-sm">
        <caption className="sr-only">{caption}</caption>
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
            {showClaimed ? (
              <th scope="col" className={COLUMN_HEADER}>
                Claimed
              </th>
            ) : null}
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
              {showClaimed ? (
                <td className="px-4 py-3 whitespace-nowrap">
                  {claimAge(assignmentOf(item)) ?? (
                    <span className="text-muted-foreground">Not known</span>
                  )}
                </td>
              ) : null}
              <td className="px-4 py-3">
                <Link
                  to="/agent/disputes/$disputeId"
                  params={{ disputeId: item.id }}
                  className={buttonVariants({ variant: LINK_VARIANT[item.status], size: "sm" })}
                >
                  {/* The space sits outside the sr-only span: a flex item drops its leading space. */}
                  {action === "read" && reviewedByAnother(assignmentOf(item)) ? "Read" : "Review"}{" "}
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
