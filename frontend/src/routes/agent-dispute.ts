import { createRoute, lazyRouteComponent } from "@tanstack/react-router";

import { ListSkeleton } from "@/components/list-skeleton";

import { rootRoute } from "./root";

/** /agent/disputes/$disputeId : one dispute for review, and the transition form. Agents only. */
export const agentDisputeRoute = createRoute({
  getParentRoute: () => rootRoute,
  staticData: { area: "agent" },
  path: "/agent/disputes/$disputeId",
  component: lazyRouteComponent(
    () => import("@/components/agent-dispute-page"),
    "AgentDisputePage",
  ),
  pendingComponent: ListSkeleton,
});
