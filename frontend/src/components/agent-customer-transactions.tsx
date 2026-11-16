import { useMemo } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";

import {
  agentCustomerTransactionsInfiniteOptions,
  type AgentCustomerTransaction,
} from "@/api/agent";
import { PagedResults } from "@/components/paged-results";
import { DisputeStatusPill, TransactionStatusPill } from "@/components/status-pill";
import { TableCard } from "@/components/table-card";
import { Amount } from "@/components/transactions-table";
import { Badge } from "@/components/ui/badge";
import { formatDate } from "@/lib/format";
import { AMOUNT_CELL, CARD, COLUMN_HEADER, TABLE_ROW } from "@/lib/table-styles";

const HEADING_ID = "customer-transactions-heading";

/**
 * What an agent needs to judge "charged twice" or "I don't recognise this": the other transactions
 * of the customer behind the dispute. Read only, newest first, and no counts or totals: the API
 * returns none. The server scopes it to this dispute and audits every page read.
 */
export function AgentCustomerTransactions({
  disputeId,
  transactionId,
}: {
  disputeId: string;
  /** The disputed transaction: its row is this dispute's, whichever dispute is now its latest. */
  transactionId: string;
}) {
  const options = useMemo(() => agentCustomerTransactionsInfiniteOptions(disputeId), [disputeId]);
  const query = useInfiniteQuery(options);
  // Every page carries the same window start; the first one is enough, and it is unknown until it loads.
  const from = query.data?.pages[0]?.from;

  return (
    <section aria-labelledby={HEADING_ID} className={CARD}>
      <div className="flex flex-col gap-1">
        <h2 id={HEADING_ID} className="text-lg font-semibold">
          {from ? `Transactions since ${formatDate(from)}` : "Customer transactions"}
        </h2>
        <p className="text-sm text-muted-foreground">
          Newest first, read only. Goes back 90 days, or to the disputed transaction if that is
          older. Dates are shown in UTC.
        </p>
      </div>
      <PagedResults
        query={query}
        queryKey={options.queryKey}
        filtersKey={disputeId}
        label="Customer transactions list"
        noun={{ one: "transaction", many: "transactions" }}
        titles={{
          firstLoadFailed: "We could not load this customer's transactions",
          nextPageFailed: "We could not load more transactions",
        }}
        empty={
          <div className="rounded-xl border p-8 text-center">
            <p className="font-medium">There are no transactions to show for this period</p>
          </div>
        }
        renderItems={(items) => (
          <CustomerTransactionsTable items={items} transactionId={transactionId} />
        )}
      />
    </section>
  );
}

function CustomerTransactionsTable({
  items,
  transactionId,
}: {
  items: readonly AgentCustomerTransaction[];
  transactionId: string;
}) {
  return (
    <TableCard label="Customer transactions table" flat>
      <table className="w-full min-w-[44rem] border-collapse text-sm">
        <caption className="sr-only">The customer&apos;s transactions, newest first</caption>
        <thead>
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
          </tr>
        </thead>
        <tbody className="divide-y">
          {items.map((item) => (
            <tr key={item.id} className={TABLE_ROW}>
              <td className="px-4 py-3 whitespace-nowrap">
                <time dateTime={item.transactionDate}>{formatDate(item.transactionDate)}</time>
              </td>
              <th scope="row" className="px-4 py-3 text-left font-normal">
                <div className="font-medium">{item.merchantName}</div>
                <div className="text-xs text-muted-foreground">
                  {item.category} &middot; Ref {item.reference}
                </div>
              </th>
              <td className="px-4 py-3 whitespace-nowrap">{item.maskedAccountNumber}</td>
              <td className={AMOUNT_CELL}>
                <Amount item={item} />
              </td>
              <td className="px-4 py-3">
                <div className="flex flex-col items-start gap-1">
                  {/* Words, not colour: the row this page is about is named. */}
                  {item.id === transactionId ? <Badge variant="outline">This dispute</Badge> : null}
                  <TransactionStatusPill status={item.status} />
                  {item.dispute ? (
                    <DisputeStatusPill status={item.dispute.status} prefix="Dispute" />
                  ) : null}
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </TableCard>
  );
}
