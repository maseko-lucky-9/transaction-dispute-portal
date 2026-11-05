import { createRoute, lazyRouteComponent, stripSearchParams } from "@tanstack/react-router";

import { ListSkeleton } from "@/components/list-skeleton";
import { DEFAULT_LIMIT, transactionsSearchSchema } from "@/lib/transaction-filters";

import { rootRoute } from "./root";

/** /transactions?from=&to=&status=&limit= : every param is validated, none can crash the page. */
export const transactionsRoute = createRoute({
  getParentRoute: () => rootRoute,
  staticData: { area: "customer" },
  path: "/transactions",
  validateSearch: transactionsSearchSchema,
  // validateSearch fills in limit=25; without this every navigation would write it to the URL.
  search: { middlewares: [stripSearchParams({ limit: DEFAULT_LIMIT })] },
  // Form, table and validation code load only when this page is opened.
  component: lazyRouteComponent(() => import("@/components/transactions-page"), "TransactionsPage"),
  pendingComponent: ListSkeleton,
});
