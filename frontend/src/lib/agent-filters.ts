import type { DisputeStatus } from "@/api/disputes";
import { z } from "@/lib/zod";

import { DISPUTE_STATUSES, DISPUTE_STATUS_LABELS } from "./dispute-filters";
import { DEFAULT_LIMIT, MAX_LIMIT, MIN_LIMIT } from "./transaction-filters";

/** "open" is every dispute still awaiting a decision (Submitted and Under review). */
export const QUEUE_OPEN = "open";
export const QUEUE_STATUSES = [QUEUE_OPEN, ...DISPUTE_STATUSES] as const;
export type QueueStatus = (typeof QUEUE_STATUSES)[number];

export const QUEUE_STATUS_LABELS: Record<QueueStatus, string> = {
  [QUEUE_OPEN]: "Open (awaiting a decision)",
  ...DISPUTE_STATUS_LABELS,
};

export interface QueueFilters {
  status: QueueStatus;
  limit: number;
}

/**
 * The queue's URL: `status` is "open" (the default, kept off the URL) or one status; `limit` as
 * on every list. Each field falls back on its own, so no link can crash the page.
 */
export const agentQueueSearchSchema = z.object({
  status: z.enum(QUEUE_STATUSES).catch(QUEUE_OPEN).default(QUEUE_OPEN),
  limit: z.number().int().min(MIN_LIMIT).max(MAX_LIMIT).catch(DEFAULT_LIMIT).default(DEFAULT_LIMIT),
});

/** The statuses a queue filter stands for, for the API (open is two). */
export const statusesOf = (status: QueueStatus): readonly DisputeStatus[] =>
  status === QUEUE_OPEN ? ["Submitted", "UnderReview"] : [status];
