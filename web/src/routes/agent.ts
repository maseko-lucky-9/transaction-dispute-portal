import { createRoute, lazyRouteComponent } from "@tanstack/react-router";

import { ListSkeleton } from "@/components/list-skeleton";

import { rootRoute } from "./root";

/** /agent : the back-office review queue. Agents only (RoleGuard). */
export const agentQueueRoute = createRoute({
  getParentRoute: () => rootRoute,
  staticData: { area: "agent" },
  path: "/agent",
  component: lazyRouteComponent(() => import("@/components/agent-queue-page"), "AgentQueuePage"),
  pendingComponent: ListSkeleton,
});
