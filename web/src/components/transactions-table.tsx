import { useId } from "react";
import { Link } from "@tanstack/react-router";

import type { TransactionItem } from "@/api/transactions";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { describeBlockReasons } from "@/lib/dispute-reasons";
import { formatDate, formatMoney } from "@/lib/format";

const COLUMN_HEADER = "px-4 py-3 text-left font-medium text-muted-foreground";

export function TransactionsTable({ items }: { items: readonly TransactionItem[] }) {
  return (
    // A scrollable region must be reachable by keyboard (axe: scrollable-region-focusable).
    <div
      role="region"
      aria-label="Transactions table"
      tabIndex={0}
      className="overflow-x-auto rounded-xl border"
    >
      <table className="w-full min-w-[44rem] border-collapse text-sm">
        <caption className="sr-only">Your transactions, newest first</caption>
        <thead className="border-b bg-muted/50">
          <tr>
            <th scope="col" className={COLUMN_HEADER}>
              Date (UTC)
            </th>
            <th scope="col" className={COLUMN_HEADER}>
              Merchant
            </th>
            <th scope="col" className={COLUMN_HEADER}>
              Account
            </th>
            <th scope="col" className={`${COLUMN_HEADER} text-right`}>
              Amount
            </th>
            <th scope="col" className={COLUMN_HEADER}>
              Status
            </th>
            <th scope="col" className={COLUMN_HEADER}>
              Dispute
            </th>
          </tr>
        </thead>
        <tbody className="divide-y">
          {items.map((item) => (
            <TransactionRow key={item.id} item={item} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

function TransactionRow({ item }: { item: TransactionItem }) {
  return (
    <tr className="align-top">
      <td className="px-4 py-3 whitespace-nowrap">
        <time dateTime={item.transactionDate}>{formatDate(item.transactionDate)}</time>
      </td>
      <th scope="row" className="px-4 py-3 text-left font-normal">
        <div className="font-medium">{item.merchantName}</div>
        <div className="text-xs text-muted-foreground">
          {item.category} &middot; Ref {item.reference}
        </div>
      </th>
      <td className="px-4 py-3 whitespace-nowrap">{item.account.maskedNumber}</td>
      <td className="px-4 py-3 text-right whitespace-nowrap">
        <Amount item={item} />
      </td>
      <td className="px-4 py-3">
        <div className="flex flex-col items-start gap-1">
          <Badge variant={item.status === "Posted" ? "secondary" : "outline"}>{item.status}</Badge>
          {item.dispute ? <Badge>Dispute: {item.dispute.status}</Badge> : null}
        </div>
      </td>
      <td className="px-4 py-3">
        <DisputeAction item={item} />
      </td>
    </tr>
  );
}

/** Sign, colour and a text label: credits never rely on colour alone. */
function Amount({ item }: { item: TransactionItem }) {
  const isCredit = item.direction === "Credit";
  return (
    <>
      <div className={isCredit ? "font-semibold text-positive" : "font-medium"}>
        {isCredit ? "+" : "−"}
        {formatMoney(item.amount, item.currency)}
      </div>
      <div className="text-xs text-muted-foreground">{item.direction}</div>
    </>
  );
}

function DisputeAction({ item }: { item: TransactionItem }) {
  const noteId = useId();
  const label = `Dispute ${item.merchantName}, ${formatDate(item.transactionDate)}`;

  if (item.isDisputable) {
    return (
      <Button asChild variant="outline" size="sm">
        <Link
          to="/transactions/$transactionId/dispute"
          params={{ transactionId: item.id }}
          aria-label={label}
        >
          Dispute
        </Link>
      </Button>
    );
  }

  const reasons = describeBlockReasons(item.disputeBlockReasons);
  return (
    <div className="flex flex-col items-start gap-1">
      {/* aria-disabled (not `disabled`) keeps it focusable, so the reason is announced. */}
      <Button
        variant="outline"
        size="sm"
        aria-disabled="true"
        aria-label={label}
        aria-describedby={noteId}
      >
        Dispute
      </Button>
      <p id={noteId} className="max-w-56 text-xs text-muted-foreground">
        {reasons.map((reason) => (
          <span key={reason} className="block">
            {reason}
          </span>
        ))}
      </p>
      {item.dispute ? (
        <Link
          to="/disputes/$disputeId"
          params={{ disputeId: item.dispute.id }}
          aria-label={`View dispute for ${item.merchantName}, ${formatDate(item.transactionDate)}`}
          className="text-xs underline underline-offset-4"
        >
          View dispute
        </Link>
      ) : null}
    </div>
  );
}
