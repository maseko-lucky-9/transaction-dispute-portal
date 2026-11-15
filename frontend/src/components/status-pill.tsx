import type { DisputeStatus } from "@/api/disputes";
import { Badge } from "@/components/ui/badge";
import { DISPUTE_STATUS_LABELS } from "@/lib/dispute-filters";
import {
  DISPUTE_STATUS_STYLES,
  TRANSACTION_STATUS_STYLES,
  type StatusStyle,
} from "@/lib/status-styles";
import { TRANSACTION_STATUS_LABELS, type TransactionStatus } from "@/lib/transaction-filters";

interface PillProps {
  status: string;
  label: string;
  style: StatusStyle;
}

/** The status word (never colour alone) as ONE text node, beside a decorative icon. */
function StatusPill({ status, label, style }: PillProps) {
  const Icon = style.icon;
  return (
    <Badge variant={style.variant} data-status={status}>
      <Icon aria-hidden="true" className="size-3" />
      {label}
    </Badge>
  );
}

/** `prefix` renders "Prefix: Label" (still one text node), e.g. "Dispute: Under review". */
export function DisputeStatusPill({ status, prefix }: { status: DisputeStatus; prefix?: string }) {
  const label = DISPUTE_STATUS_LABELS[status];
  return (
    <StatusPill
      status={status}
      label={prefix ? `${prefix}: ${label}` : label}
      style={DISPUTE_STATUS_STYLES[status]}
    />
  );
}

export function TransactionStatusPill({ status }: { status: TransactionStatus }) {
  return (
    <StatusPill
      status={status}
      label={TRANSACTION_STATUS_LABELS[status]}
      style={TRANSACTION_STATUS_STYLES[status]}
    />
  );
}
