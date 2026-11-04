import { rootRoute } from "./routes/root";
import { indexRoute } from "./routes/index";
import { transactionsRoute } from "./routes/transactions";
import { disputesRoute } from "./routes/disputes";
import { disputeDetailRoute } from "./routes/dispute-detail";
import { disputeNewRoute } from "./routes/dispute-new";

export const routeTree = rootRoute.addChildren([
  indexRoute,
  transactionsRoute,
  disputesRoute,
  disputeDetailRoute,
  disputeNewRoute,
]);
