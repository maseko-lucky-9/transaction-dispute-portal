import { useQuery } from "@tanstack/react-query";
import { getRouteApi, Link } from "@tanstack/react-router";

import { disputeReasonsOptions } from "@/api/disputes";
import { isApiError } from "@/api/problem";
import { transactionOptions, type TransactionItem } from "@/api/transactions";
import { DisputeForm } from "@/components/dispute-form";
import { ErrorState } from "@/components/error-state";
import { Fact } from "@/components/fact";
import { Skeleton } from "@/components/ui/skeleton";
import { describeBlockReasons } from "@/lib/dispute-reasons";
import { formatDate, formatMoney } from "@/lib/format";
import { isGuid } from "@/lib/guid";

// getRouteApi avoids importing the route here (the route imports this component).
const route = getRouteApi("/transactions/$transactionId/dispute");

export function DisputeFormPage() {
  const { transactionId } = route.useParams();
  const validId = isGuid(transactionId);
  // A malformed id cannot be a transaction: no request, same page as an unknown id.
  const transaction = useQuery({ ...transactionOptions(transactionId), enabled: validId });
  const reasons = useQuery(disputeReasonsOptions);

  if (!validId || (isApiError(transaction.error) && transaction.error.status === 404)) {
    return <NotFoundTransaction />;
  }
  if (transaction.isPending) {
    return (
      <div role="status" aria-busy="true" className="flex max-w-2xl flex-col gap-4">
        <span className="sr-only">Loading transaction...</span>
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-24 w-full" />
        <Skeleton className="h-48 w-full" />
      </div>
    );
  }
  if (!transaction.data) {
    return (
      <ErrorState
        error={transaction.error}
        title="We could not load this transaction"
        headingLevel="h1"
        onRetry={() => void transaction.refetch()}
      />
    );
  }

  const item = transaction.data;
  return (
    <div className="flex max-w-2xl flex-col gap-6">
      <title>Dispute a transaction - Transactions Dispute Portal</title>
      <div className="flex flex-col gap-2">
        <Link to="/transactions" className="text-sm underline underline-offset-4">
          Back to my transactions
        </Link>
        <h1 className="text-2xl font-semibold">Dispute a transaction</h1>
      </div>

      <section aria-labelledby="transaction-heading" className="flex flex-col gap-3">
        <h2 id="transaction-heading" className="text-lg font-semibold">
          The transaction
        </h2>
        <dl className="grid gap-x-8 gap-y-3 sm:grid-cols-2">
          <Fact term="Merchant" value={item.merchantName} />
          <Fact term="Reference" value={item.reference} />
          <Fact term="Amount" value={formatMoney(item.amount, item.currency)} />
          <Fact term="Date (UTC)" value={formatDate(item.transactionDate)} />
        </dl>
      </section>

      {!item.isDisputable ? (
        <NotDisputable item={item} />
      ) : reasons.data ? (
        <DisputeForm key={item.id} transaction={item} reasons={reasons.data} />
      ) : reasons.isPending ? (
        <div role="status" aria-busy="true" className="flex flex-col gap-3">
          <span className="sr-only">Loading the form...</span>
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-28 w-full" />
        </div>
      ) : (
        <ErrorState
          error={reasons.error}
          title="We could not load the dispute reasons"
          onRetry={() => void reasons.refetch()}
        />
      )}
    </div>
  );
}

/** The list already said so; this is what a bookmarked or shared link to the form shows. */
function NotDisputable({ item }: { item: TransactionItem }) {
  return (
    <section
      aria-labelledby="blocked-heading"
      className="flex flex-col gap-3 rounded-xl border p-4"
    >
      <h2 id="blocked-heading" className="font-semibold">
        This transaction can&apos;t be disputed
      </h2>
      <ul className="list-disc pl-5 text-sm">
        {describeBlockReasons(item.disputeBlockReasons).map((reason) => (
          <li key={reason}>{reason}</li>
        ))}
      </ul>
      {item.dispute ? (
        <Link
          to="/disputes/$disputeId"
          params={{ disputeId: item.dispute.id }}
          className="text-sm underline underline-offset-4"
        >
          View the dispute
        </Link>
      ) : null}
    </section>
  );
}

function NotFoundTransaction() {
  return (
    <div className="flex flex-col items-start gap-3">
      <h1 className="text-2xl font-semibold">Transaction not found</h1>
      <p className="text-muted-foreground">
        We could not find that transaction. It may belong to someone else or no longer exist.
      </p>
      <Link to="/transactions" className="underline underline-offset-4">
        Back to my transactions
      </Link>
    </div>
  );
}
