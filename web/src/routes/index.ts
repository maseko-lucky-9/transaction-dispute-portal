import { createRoute } from "@tanstack/react-router";
import { z } from "zod";

import { HomePage } from "@/components/home-page";

import { rootRoute } from "./root";

/**
 * THE PATTERN FOR EVERY ROUTE: declare its search params as a zod schema and hand it to
 * `validateSearch`. Router state, `<Link search>` and `useSearch` are then typed from it, and
 * a hand-edited URL is parsed, never trusted. For list pages use per-field `.catch(default)`
 * so an invalid value falls back instead of crashing, e.g.
 *
 *   z.object({ limit: z.number().int().min(1).max(100).catch(25) })
 *
 * Home takes no params, so its schema is empty. (Unknown keys stay on the URL but are not part
 * of the typed search; use the router's `stripSearchParams` middleware if one must be removed.)
 */
export const homeSearchSchema = z.object({});

export const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  validateSearch: homeSearchSchema,
  component: HomePage,
});
