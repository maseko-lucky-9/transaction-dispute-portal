import { render, screen } from "@testing-library/react";
import {
  createMemoryHistory,
  createRootRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router";
import { describe, expect, it } from "vitest";

import { WrongArea } from "@/components/wrong-area";
import type { Access, Area } from "@/lib/roles";

async function show(required: Area, access: Access) {
  const router = createRouter({
    routeTree: createRootRoute({
      component: () => <WrongArea required={required} access={access} />,
    }),
    history: createMemoryHistory({ initialEntries: ["/"] }),
  });
  render(<RouterProvider router={router} />);
  return screen.findByRole("heading", { level: 1 });
}

describe("WrongArea", () => {
  it("tells a customer the back-office is not theirs and links home", async () => {
    expect(await show("agent", "customer")).toHaveTextContent(
      "This area is for back-office agents",
    );
    expect(screen.getByRole("link", { name: "Back to home" })).toHaveAttribute("href", "/");
    expect(document.title).toBe(
      "This area is for back-office agents - Transactions Dispute Portal",
    );
  });

  it("tells an agent the customer pages are not theirs and links to the queue", async () => {
    expect(await show("customer", "agent")).toHaveTextContent("This area is for customers");
    expect(screen.getByRole("link", { name: "Go to the review queue" })).toHaveAttribute(
      "href",
      "/agent",
    );
  });

  it("explains a both-roles account the same way whichever area was asked for", async () => {
    expect(await show("agent", "both")).toHaveTextContent(
      "Your account has both a customer and a back-office role",
    );
    expect(screen.getByText(/Ask an administrator/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back to home" })).toBeInTheDocument();
  });
});
