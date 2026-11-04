import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { z } from "zod";

/**
 * Pins the validateSearch pattern that every list page will copy (see routes/index.ts):
 * zod 4 schema -> `validateSearch` -> typed `useSearch`, with per-field `.catch` fallbacks.
 * A throwaway route is used so the pattern is tested before the real pages exist.
 */
const searchSchema = z.object({
  limit: z.number().int().min(1).max(100).catch(25),
  status: z.enum(["open", "closed"]).optional().catch(undefined),
});

const rootRoute = createRootRoute();
const listRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/list",
  validateSearch: searchSchema,
  component: function ListProbe() {
    return <output>{JSON.stringify(listRoute.useSearch())}</output>;
  },
});

async function searchFor(url: string): Promise<unknown> {
  const router = createRouter({
    routeTree: rootRoute.addChildren([listRoute]),
    history: createMemoryHistory({ initialEntries: [url] }),
  });
  render(<RouterProvider router={router} />);
  return JSON.parse((await screen.findByRole("status")).textContent) as unknown;
}

describe("validateSearch with zod", () => {
  it("applies defaults when params are absent", async () => {
    expect(await searchFor("/list")).toEqual({ limit: 25 });
  });

  it("passes valid params through", async () => {
    expect(await searchFor("/list?limit=50&status=closed")).toEqual({
      limit: 50,
      status: "closed",
    });
  });

  it.each(["0", "101", "abc", "-5", "2.5"])(
    "falls back to the default for an invalid limit (%s)",
    async (limit) => {
      expect(await searchFor(`/list?limit=${limit}`)).toEqual({ limit: 25 });
    },
  );

  it("drops an invalid enum value instead of crashing", async () => {
    const search = await searchFor("/list?status=hacked");
    expect(search).toEqual({ limit: 25 });
  });
});
