import type { DisputeStatus } from "@/api/disputes";
import { Badge } from "@/components/ui/badge";
import { DISPUTE_STATUS_LABELS } from "@/lib/dispute-filters";

const VARIANTS: Record<DisputeStatus, "default" | "secondary" | "destructive" | "outline"> = {
  Submitted: "outline",
  UnderReview: "secondary",
  Accepted: "default",
  Rejected: "destructive",
  Withdrawn: "outline",
};

/** The status as a word (never colour alone) in a badge. */
export function DisputeStatusBadge({ status }: { status: DisputeStatus }) {
  return <Badge variant={VARIANTS[status]}>{DISPUTE_STATUS_LABELS[status]}</Badge>;
}
