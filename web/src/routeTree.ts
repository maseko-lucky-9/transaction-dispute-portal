import { rootRoute } from "./routes/root";
import { indexRoute } from "./routes/index";
import { transactionsRoute } from "./routes/transactions";

export const routeTree = rootRoute.addChildren([indexRoute, transactionsRoute]);
