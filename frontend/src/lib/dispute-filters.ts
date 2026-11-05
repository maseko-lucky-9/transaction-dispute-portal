import { z } from "@/lib/zod";

import type { DisputeStatus } from "@/api/disputes";

import { DEFAULT_LIMIT, MAX_LIMIT, MIN_LIMIT } from "./transaction-filters";

/**
 * Filters for the dispute history. Same philosophy as the transactions page: the URL is
 * parsed, never trusted, and every field falls back on its own so no link can crash the page.
 */
export const DISPUTE_STATUSES = [
  "Submitted",
  "UnderReview",
  "Accepted",
  "Rejected",
  "Withdrawn",
] as const satisfies readonly DisputeStatus[];

/** What a customer reads: "UnderReview" is a code, not a word. */
export const DISPUTE_STATUS_LABELS: Record<DisputeStatus, string> = {
  Submitted: "Submitted",
  UnderReview: "Under review",
  Accepted: "Accepted",
  Rejected: "Rejected",
  Withdrawn: "Withdrawn",
};

/** A dispute can be withdrawn until a decision has been made. */
export const WITHDRAWABLE_STATUSES: readonly DisputeStatus[] = ["Submitted", "UnderReview"];

export interface DisputeFilters {
  status?: DisputeStatus | undefined;
  limit: number;
}

export const disputesSearchSchema = z.object({
  status: z.enum(DISPUTE_STATUSES).optional().catch(undefined),
  limit: z.number().int().min(MIN_LIMIT).max(MAX_LIMIT).catch(DEFAULT_LIMIT).default(DEFAULT_LIMIT),
});
