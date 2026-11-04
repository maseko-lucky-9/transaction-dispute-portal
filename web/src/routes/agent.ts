import { createRoute, lazyRouteComponent, stripSearchParams } from "@tanstack/react-router";

import { ListSkeleton } from "@/components/list-skeleton";
import { agentQueueSearchSchema, QUEUE_OPEN } from "@/lib/agent-filters";
import { DEFAULT_LIMIT } from "@/lib/transaction-filters";

import { rootRoute } from "./root";

/** /agent?status=&limit= : the back-office review queue. Agents only (RoleGuard). */
export const agentQueueRoute = createRoute({
  getParentRoute: () => rootRoute,
  staticData: { area: "agent" },
  path: "/agent",
  validateSearch: agentQueueSearchSchema,
  search: { middlewares: [stripSearchParams({ status: QUEUE_OPEN, limit: DEFAULT_LIMIT })] },
  component: lazyRouteComponent(() => import("@/components/agent-queue-page"), "AgentQueuePage"),
  pendingComponent: ListSkeleton,
});
