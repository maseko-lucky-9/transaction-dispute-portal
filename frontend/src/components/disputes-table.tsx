import { Link } from "@tanstack/react-router";

import type { DisputeItem, DisputeReason } from "@/api/disputes";
import { TableCard } from "@/components/table-card";
import { DisputeStatusPill } from "@/components/status-pill";
import { buttonVariants } from "@/components/ui/button";
import { reasonLabel } from "@/lib/dispute-reasons-catalogue";
import { formatDate, formatMoney } from "@/lib/format";
import { AMOUNT_CELL, COLUMN_HEADER, TABLE_ROW } from "@/lib/table-styles";

interface DisputesTableProps {
  items: readonly DisputeItem[];
  /** For readable reason labels; the table still works (humanised codes) without it. */
  reasons: readonly DisputeReason[] | undefined;
}

export function DisputesTable({ items, reasons }: DisputesTableProps) {
  return (
    <TableCard label="Disputes table" flat>
      <table className="w-full min-w-[40rem] border-collapse text-sm">
        <caption className="sr-only">Your disputes, newest first</caption>
        <thead>
          <tr>
            <th scope="col" className={COLUMN_HEADER}>
              Raised (UTC)
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
              <th scope="row" className="px-4 py-3 text-left font-medium">
                {item.merchantName}
              </th>
              <td className={AMOUNT_CELL}>{formatMoney(item.amount, item.currency)}</td>
              <td className="px-4 py-3">{reasonLabel(item.reasonCode, reasons)}</td>
              <td className="px-4 py-3">
                <DisputeStatusPill status={item.status} />
              </td>
              <td className="px-4 py-3">
                <Link
                  to="/disputes/$disputeId"
                  params={{ disputeId: item.id }}
                  aria-label={`View dispute for ${item.merchantName}, ${formatDate(item.createdAt)}`}
                  className={buttonVariants({ variant: "outline", size: "sm" })}
                >
                  View
                </Link>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </TableCard>
  );
}
