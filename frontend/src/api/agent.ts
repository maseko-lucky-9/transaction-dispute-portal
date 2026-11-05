import { infiniteQueryOptions, queryOptions } from "@tanstack/react-query";

import { QUEUE_OPEN, statusesOf, type QueueFilters } from "@/lib/agent-filters";
import type { TransitionTarget } from "@/lib/transitions";

import { apiFetch, apiRequest } from "./client";
import type { components, operations } from "./schema";

type Schemas = components["schemas"];
export type AgentQueueItem = Schemas["AgentQueueItem"];
export type AgentQueuePage = Schemas["AgentQueueResponse"];
export type AgentDisputeDetail = Schemas["AgentDisputeDetail"];
export type TransitionRequest = Schemas["TransitionRequest"];
export type ListAgentQueueQuery = NonNullable<operations["ListAgentQueue"]["parameters"]["query"]>;

/** An agent's view of a dispute with the ETag it was read with: a transition must echo it. */
export interface AgentDisputeWithEtag {
  dispute: AgentDisputeDetail;
  etag: string | null;
}

export const agentKeys = {
  queue: (filters: QueueFilters) => ["agent", "queue", filters] as const,
  detail: (id: string) => ["agent", "detail", id] as const,
};

const withEtag = (response: {
  data: AgentDisputeDetail;
  headers: Headers;
}): AgentDisputeWithEtag => ({
  dispute: response.data,
  etag: response.headers.get("ETag"),
});

/** `GET /api/v1/agent/disputes`: oldest first. No status in the request means the open queue. */
export function fetchAgentQueue(
  filters: QueueFilters,
  cursor: string | undefined,
  signal?: AbortSignal,
): Promise<AgentQueuePage> {
  const statuses = statusesOf(filters.status);
  const query = {
    limit: filters.limit,
    status: filters.status === QUEUE_OPEN ? undefined : statuses[0],
    cursor,
  } satisfies ListAgentQueueQuery;
  const params = new URLSearchParams();
  for (const [name, value] of Object.entries(query)) {
    if (value !== undefined && value !== "") params.set(name, String(value));
  }
  return apiFetch<AgentQueuePage>(
    `/api/v1/agent/disputes?${params.toString()}`,
    signal ? { signal } : {},
  );
}

export function agentQueueInfiniteOptions(filters: QueueFilters) {
  return infiniteQueryOptions({
    queryKey: agentKeys.queue(filters),
    queryFn: ({ pageParam, signal }) => fetchAgentQueue(filters, pageParam, signal),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
    refetchOnWindowFocus: false,
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
