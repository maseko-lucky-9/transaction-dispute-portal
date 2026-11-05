import type { ReactNode } from "react";

import type { DisputeDetail, DisputeReason } from "@/api/disputes";
import { Fact } from "@/components/fact";
import { reasonLabel } from "@/lib/dispute-reasons-catalogue";
import { formatDate, formatDateTime, formatMoney } from "@/lib/format";

interface DisputeOverviewProps {
  dispute: Pick<
    DisputeDetail,
    | "amount"
    | "currency"
    | "reasonCode"
    | "updatedAt"
    | "resolvedAt"
    | "description"
    | "resolutionNote"
    | "transaction"
  >;
  reasons: readonly DisputeReason[] | undefined;
  /** "Your dispute" for the customer, "The dispute" for an agent. */
  summaryHeading: string;
  /** Whose words the description is: "What you told us", "What the customer wrote". */
  descriptionHeading: string;
  /** Extra facts at the top of the summary (the customer's name, for an agent). */
  extraFacts?: ReactNode;
  /** The timeline section, already built for this viewer. */
  timeline: ReactNode;
}

/**
 * What a customer and an agent both see of a dispute: the summary with the description and the
 * resolution note, the transaction, and the timeline section. The pages around it differ.
 */
export function DisputeOverview(props: DisputeOverviewProps) {
  const { dispute } = props;
  const { transaction } = dispute;
  return (
    <>
      <section aria-labelledby="summary-heading" className="flex flex-col gap-3">
        <h2 id="summary-heading" className="text-lg font-semibold">
          {props.summaryHeading}
        </h2>
        <dl className="grid gap-x-8 gap-y-3 sm:grid-cols-2">
          {props.extraFacts}
          <Fact term="Amount disputed" value={formatMoney(dispute.amount, dispute.currency)} />
          <Fact term="Reason" value={reasonLabel(dispute.reasonCode, props.reasons)} />
          <Fact term="Last updated" value={formatDateTime(dispute.updatedAt)} />
          {dispute.resolvedAt ? (
            <Fact term="Closed" value={formatDateTime(dispute.resolvedAt)} />
          ) : null}
        </dl>
        <div className="flex flex-col gap-1">
          <h3 className="text-sm font-medium text-muted-foreground">{props.descriptionHeading}</h3>
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
        {props.timeline}
      </section>
    </>
  );
}
