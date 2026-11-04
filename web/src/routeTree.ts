import { rootRoute } from "./routes/root";
import { indexRoute } from "./routes/index";

export const routeTree = rootRoute.addChildren([indexRoute]);
