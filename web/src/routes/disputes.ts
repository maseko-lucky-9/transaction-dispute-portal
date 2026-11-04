import { createRoute, lazyRouteComponent, stripSearchParams } from "@tanstack/react-router";

import { ListSkeleton } from "@/components/list-skeleton";
import { disputesSearchSchema } from "@/lib/dispute-filters";
import { DEFAULT_LIMIT } from "@/lib/transaction-filters";

import { rootRoute } from "./root";

/** /disputes?status=&limit= : the history. Every param is validated, none can crash the page. */
export const disputesRoute = createRoute({
  getParentRoute: () => rootRoute,
  staticData: { area: "customer" },
  path: "/disputes",
  validateSearch: disputesSearchSchema,
  search: { middlewares: [stripSearchParams({ limit: DEFAULT_LIMIT })] },
  component: lazyRouteComponent(() => import("@/components/disputes-page"), "DisputesPage"),
  pendingComponent: ListSkeleton,
});
