import { createRoute, lazyRouteComponent } from "@tanstack/react-router";

import { ListSkeleton } from "@/components/list-skeleton";

import { rootRoute } from "./root";

/** /transactions/$transactionId/dispute : the form that raises a dispute on one transaction. */
export const disputeNewRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/transactions/$transactionId/dispute",
  component: lazyRouteComponent(() => import("@/components/dispute-form-page"), "DisputeFormPage"),
  pendingComponent: ListSkeleton,
});
