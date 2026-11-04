import { createRoute, lazyRouteComponent } from "@tanstack/react-router";

import { ListSkeleton } from "@/components/list-skeleton";

import { rootRoute } from "./root";

/** /disputes/$disputeId : one dispute, its timeline and the withdraw action. */
export const disputeDetailRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/disputes/$disputeId",
  component: lazyRouteComponent(
    () => import("@/components/dispute-detail-page"),
    "DisputeDetailPage",
  ),
  pendingComponent: ListSkeleton,
});
