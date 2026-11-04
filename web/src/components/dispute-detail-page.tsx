import { useQuery } from "@tanstack/react-query";
import { getRouteApi, Link, useLocation } from "@tanstack/react-router";

import { disputeDetailOptions, disputeReasonsOptions } from "@/api/disputes";
import { isApiError } from "@/api/problem";
import { DisputeStatusBadge } from "@/components/dispute-status-badge";
import { DisputeTimeline } from "@/components/dispute-timeline";
import { ErrorState } from "@/components/error-state";
import { Fact } from "@/components/fact";
import { Skeleton } from "@/components/ui/skeleton";
import { WithdrawDispute } from "@/components/withdraw-dispute";
import { reasonLabel } from "@/lib/dispute-reasons-catalogue";
import { formatDate, formatDateTime, formatMoney } from "@/lib/format";
import { isGuid } from "@/lib/guid";

// getRouteApi avoids importing the route here (the route imports this component).
const route = getRouteApi("/disputes/$disputeId");

function focusOnMount(element: HTMLElement | null) {
  element?.focus();
}

export function DisputeDetailPage() {
  const { disputeId } = route.useParams();
  const justSubmitted = useLocation({ select: (location) => location.state.disputeSubmitted });
  const earlierDetailsKept = useLocation({
    select: (location) => location.state.earlierDetailsKept,
  });
  const validId = isGuid(disputeId);
  // A malformed id cannot be a dispute: no request, same page as an unknown id.
  const query = useQuery({ ...disputeDetailOptions(disputeId), enabled: validId });
  const reasons = useQuery(disputeReasonsOptions);

  if (!validId || (isApiError(query.error) && query.error.status === 404)) {
    return <NotFoundDispute />;
  }
  if (query.isPending) {
    return (
      <div role="status" aria-busy="true" className="flex flex-col gap-4">
        <span className="sr-only">Loading dispute...</span>
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-32 w-full" />
        <Skeleton className="h-48 w-full" />
      </div>
    );
  }
  if (!query.data) {
    return (
      <ErrorState
        error={query.error}
        title="We could not load this dispute"
        headingLevel="h1"
        onRetry={() => void query.refetch()}
      />
    );
  }

  const { dispute, etag } = query.data;
  const { transaction } = dispute;

  return (
    <div className="flex flex-col gap-8">
      <title>Dispute - Transactions Dispute Portal</title>

      {justSubmitted ? (
        // One live region, focused, so the whole message is heard at once.
        <div
          role="status"
          tabIndex={-1}
          ref={focusOnMount}
          className="flex flex-col gap-1 rounded-md border bg-muted/50 px-4 py-2 text-sm outline-none"
        >
          <p className="font-medium">
            Your dispute was submitted. We will review it and update the timeline below.
          </p>
          {earlierDetailsKept ? (
            <p>
              We had already received your request, so we kept what you sent the first time. A
              submitted dispute cannot be edited: if the text, reason or amount below is not what
              you meant, withdraw this dispute and raise a new one.
            </p>
          ) : null}
        </div>
      ) : null}

      <div className="flex flex-col gap-2">
        <Link to="/disputes" className="text-sm underline underline-offset-4">
          Back to my disputes
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-semibold">Dispute: {dispute.merchantName}</h1>
          <DisputeStatusBadge status={dispute.status} />
        </div>
        <p className="text-sm text-muted-foreground">Raised {formatDateTime(dispute.createdAt)}</p>
      </div>

      <section aria-labelledby="summary-heading" className="flex flex-col gap-3">
        <h2 id="summary-heading" className="text-lg font-semibold">
          Your dispute
        </h2>
        <dl className="grid gap-x-8 gap-y-3 sm:grid-cols-2">
          <Fact term="Amount disputed" value={formatMoney(dispute.amount, dispute.currency)} />
          <Fact term="Reason" value={reasonLabel(dispute.reasonCode, reasons.data)} />
          <Fact term="Last updated" value={formatDateTime(dispute.updatedAt)} />
          {dispute.resolvedAt ? (
            <Fact term="Closed" value={formatDateTime(dispute.resolvedAt)} />
          ) : null}
        </dl>
        <div className="flex flex-col gap-1">
          <h3 className="text-sm font-medium text-muted-foreground">What you told us</h3>
          {/* User text: rendered as text (React escapes it), keeping the line breaks. */}
          <p className="whitespace-pre-wrap">{dispute.description}</p>
        </div>
        {dispute.resolutionNote ? (
          <div className="flex flex-col gap-1 rounded-md border bg-muted/50 p-4">
            <h3 className="text-sm font-medium text-muted-foreground">Resolution note</h3>
            <p className="whitespace-pre-wrap">{dispute.resolutionNote}</p>
          </div>
        ) : null}
      </section>

      <section aria-labelledby="transaction-heading" className="flex flex-col gap-3">
        <h2 id="transaction-heading" className="text-lg font-semibold">
          The transaction
        </h2>
        <dl className="grid gap-x-8 gap-y-3 sm:grid-cols-2">
          <Fact term="Merchant" value={transaction.merchantName} />
          <Fact term="Reference" value={transaction.reference} />
          <Fact term="Amount" value={formatMoney(transaction.amount, transaction.currency)} />
          <Fact term="Date (UTC)" value={formatDate(transaction.transactionDate)} />
        </dl>
      </section>

      <section aria-labelledby="timeline-heading" className="flex flex-col gap-3">
        <h2 id="timeline-heading" className="text-lg font-semibold">
          Timeline
        </h2>
        <DisputeTimeline entries={dispute.timeline} />
      </section>

      <WithdrawDispute id={dispute.id} current={{ dispute, etag }} />
    </div>
  );
}

function NotFoundDispute() {
  return (
    <div className="flex flex-col items-start gap-3">
      <h1 className="text-2xl font-semibold">Dispute not found</h1>
      <p className="text-muted-foreground">
        We could not find that dispute. It may belong to someone else or no longer exist.
      </p>
      <Link to="/disputes" className="underline underline-offset-4">
        Back to my disputes
      </Link>
    </div>
  );
}
