import { infiniteQueryOptions, queryOptions } from "@tanstack/react-query";

import {
  QUEUE_OPEN,
  statusesOf,
  type AssignmentFilters,
  type QueueFilters,
} from "@/lib/agent-filters";
import type { TransitionTarget } from "@/lib/transitions";

import { apiFetch, apiRequest } from "./client";
import type { components, operations } from "./schema";

type Schemas = components["schemas"];
export type AgentQueueItem = Schemas["AgentQueueItem"];
export type AgentQueuePage = Schemas["AgentQueueResponse"];
export type AgentCustomerTransaction = Schemas["AgentCustomerTransaction"];
export type AgentCustomerTransactionsPage = Schemas["AgentCustomerTransactionsResponse"];
export type AgentDisputeDetail = Schemas["AgentDisputeDetail"];
export type TransitionRequest = Schemas["TransitionRequest"];
export type ListAgentQueueQuery = NonNullable<operations["ListAgentQueue"]["parameters"]["query"]>;
export type ListAgentCustomerTransactionsQuery = NonNullable<
  operations["ListAgentCustomerTransactions"]["parameters"]["query"]
>;

/** An agent's view of a dispute with the ETag it was read with: a transition must echo it. */
export interface AgentDisputeWithEtag {
  dispute: AgentDisputeDetail;
  etag: string | null;
}

export const agentKeys = {
  queue: (filters: QueueFilters | AssignmentFilters) => ["agent", "queue", filters] as const,
  detail: (id: string) => ["agent", "detail", id] as const,
  customerTransactions: (disputeId: string) =>
    ["agent", "customer-transactions", disputeId] as const,
};

const withEtag = (response: {
  data: AgentDisputeDetail;
  headers: Headers;
}): AgentDisputeWithEtag => ({
  dispute: response.data,
  etag: response.headers.get("ETag"),
});

/** The query for a filter set: a status OR an assignment, never both (the API answers 400). */
function queueQuery(
  filters: QueueFilters | AssignmentFilters,
  cursor: string | undefined,
): ListAgentQueueQuery {
  if ("assignment" in filters) {
    return { limit: filters.limit, assignment: filters.assignment, cursor };
  }
  return {
    limit: filters.limit,
    status: filters.status === QUEUE_OPEN ? undefined : statusesOf(filters.status)[0],
    cursor,
  };
}

/**
 * `GET /api/v1/agent/disputes`: oldest first. No status in the request means the open queue; an
 * assignment asks for one of the workbench's sections. An older api ignores the assignment and
 * answers with the open queue, which the workbench detects (lib/assignment.ts).
 */
export function fetchAgentQueue(
  filters: QueueFilters | AssignmentFilters,
  cursor: string | undefined,
  signal?: AbortSignal,
): Promise<AgentQueuePage> {
  const query = queueQuery(filters, cursor);
  const params = new URLSearchParams();
  for (const [name, value] of Object.entries(query)) {
    if (value !== undefined && value !== "") params.set(name, String(value));
  }
  return apiFetch<AgentQueuePage>(
    `/api/v1/agent/disputes?${params.toString()}`,
    signal ? { signal } : {},
  );
}

export function agentQueueInfiniteOptions(filters: QueueFilters | AssignmentFilters) {
  return infiniteQueryOptions({
    queryKey: agentKeys.queue(filters),
    queryFn: ({ pageParam, signal }) => fetchAgentQueue(filters, pageParam, signal),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
    refetchOnWindowFocus: false,
    staleTime: 0,
  });
}

/**
 * `GET /api/v1/agent/disputes/{id}/customer-transactions`: the transactions of the customer behind
 * a dispute, newest first, from the date in each page's `from`. The server takes the customer from
 * the dispute; nothing here names one. Every page read is audited, so no limit is sent (the default
 * page is enough) and nothing refetches on its own.
 */
export function fetchAgentCustomerTransactions(
  disputeId: string,
  cursor: string | undefined,
  signal?: AbortSignal,
): Promise<AgentCustomerTransactionsPage> {
  const query = { cursor } satisfies ListAgentCustomerTransactionsQuery;
  const params = new URLSearchParams();
  for (const [name, value] of Object.entries(query)) {
    if (value !== undefined && value !== "") params.set(name, String(value));
  }
  const search = params.size > 0 ? `?${params.toString()}` : "";
  return apiFetch<AgentCustomerTransactionsPage>(
    `/api/v1/agent/disputes/${encodeURIComponent(disputeId)}/customer-transactions${search}`,
    signal ? { signal } : {},
  );
}

export function agentCustomerTransactionsInfiniteOptions(disputeId: string) {
  return infiniteQueryOptions({
    queryKey: agentKeys.customerTransactions(disputeId),
    queryFn: ({ pageParam, signal }) =>
      fetchAgentCustomerTransactions(disputeId, pageParam, signal),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
    // Each (re)fetch of a page is an audited read of a customer's data: only when the agent opens
    // the dispute or asks for more, never on focus or on reconnect.
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
    staleTime: 0,
  });
}

/** `GET /api/v1/agent/disputes/{id}`: the dispute and the ETag to transition it with. */
export async function fetchAgentDispute(
  id: string,
  signal?: AbortSignal,
): Promise<AgentDisputeWithEtag> {
  return withEtag(
    await apiRequest<AgentDisputeDetail>(
      `/api/v1/agent/disputes/${encodeURIComponent(id)}`,
      signal ? { signal } : {},
    ),
  );
}

export const agentDisputeOptions = (id: string) =>
  queryOptions({
    queryKey: agentKeys.detail(id),
    queryFn: ({ signal }) => fetchAgentDispute(id, signal),
    // The ETag is the version the agent decides on: always revalidate on open.
    staleTime: 0,
  });

/**
 * `POST /api/v1/agent/disputes/take-next`: claim the oldest unclaimed dispute and start its
 * review. The dispute and its ETag, or null when nothing is waiting (204).
 */
export async function takeNextDispute(): Promise<AgentDisputeWithEtag | null> {
  const response = await apiRequest<AgentDisputeDetail | undefined>(
    "/api/v1/agent/disputes/take-next",
    { method: "POST" },
  );
  if (response.status === 204 || response.data === undefined) return null;
  return withEtag({ data: response.data, headers: response.headers });
}

/**
 * `POST /api/v1/agent/disputes/{id}/transitions` with `If-Match`. A missing ETag is sent as no
 * header at all: the server answers 428, which the page treats as a bug.
 */
export async function transitionDispute(
  id: string,
  etag: string | null,
  toStatus: TransitionTarget,
  note: string,
): Promise<AgentDisputeWithEtag> {
  const body: TransitionRequest = note === "" ? { toStatus } : { toStatus, note };
  return withEtag(
    await apiRequest<AgentDisputeDetail>(
      `/api/v1/agent/disputes/${encodeURIComponent(id)}/transitions`,
      { method: "POST", json: body, headers: etag ? { "If-Match": etag } : {} },
    ),
  );
}
