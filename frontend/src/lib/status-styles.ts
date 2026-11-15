import { Check, CircleDot, Clock, Minus, Undo2, X, type LucideIcon } from "lucide-react";

import type { DisputeStatus } from "@/api/disputes";

import type { TransactionStatus } from "./transaction-filters";

/** The Badge variants that carry a status colour pair (see the --status-* tokens). */
export type StatusVariant = "submitted" | "review" | "accepted" | "rejected" | "withdrawn";

export interface StatusStyle {
  variant: StatusVariant;
  icon: LucideIcon;
}

export const DISPUTE_STATUS_STYLES: Record<DisputeStatus, StatusStyle> = {
  Submitted: { variant: "submitted", icon: CircleDot },
  UnderReview: { variant: "review", icon: Clock },
  Accepted: { variant: "accepted", icon: Check },
  Rejected: { variant: "rejected", icon: X },
  Withdrawn: { variant: "withdrawn", icon: Minus },
};

/** Transactions reuse three of the dispute colours: pending = review, posted = accepted. */
export const TRANSACTION_STATUS_STYLES: Record<TransactionStatus, StatusStyle> = {
  Pending: { variant: "review", icon: Clock },
  Posted: { variant: "accepted", icon: Check },
  Reversed: { variant: "rejected", icon: Undo2 },
};
