import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { fakeAgentApi, makeAgentDispute } from "@/test/agent";
import { bob, problem, signedIn } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";
import { http } from "msw";

const STATUSES = ["Submitted", "UnderReview", "Accepted"] as const;

function disputes(count: number) {
  return Array.from({ length: count }, (_, index) =>
    makeAgentDispute({ status: STATUSES[index % 3] ?? "Submitted" }, index),
  );
}

async function openQueue(path = "/agent", count = 60) {
  const { api, handlers } = fakeAgentApi(disputes(count));
  server.use(signedIn(bob), ...handlers);
  const view = renderApp(path);
  await screen.findByRole("heading", { level: 1, name: "Review queue" });
  await screen.findByRole("table");
  return { api, ...view };
}

const rows = () => within(screen.getByRole("table")).getAllByRole("row").length - 1;

describe("agent queue", () => {
  it("lists the open queue oldest first, each row linking to its review page", async () => {
    const { api } = await openQueue();

    // "open" is the default and is NOT sent: no status means the open queue.
    expect(Object.fromEntries(api.queueRequests.at(-1)?.searchParams ?? [])).toEqual({
      limit: "25",
    });
    expect(document.title).toBe("Review queue - Transactions Dispute Portal");
    const first = within(screen.getAllByRole("row")[1] as HTMLElement);
    expect(first.getByText("Merchant 0001")).toBeInTheDocument();
    expect(first.getByText("Customer 1")).toBeInTheDocument();
    expect(first.getByRole("link", { name: /^Review/ })).toHaveAttribute(
      "href",
      "/agent/disputes/dddddddd-0000-4000-8000-000000000001",
    );
    // Open = Submitted + UnderReview only.
    const table = within(screen.getByRole("table"));
    expect(table.queryByText("Accepted")).not.toBeInTheDocument();
  });

  it("filters by status through the URL and sends the status to the API", async () => {
    const { api, router } = await openQueue();

    await userEvent.setup().selectOptions(screen.getByLabelText("Status"), "Accepted");

    await screen.findByText(/Showing 20 disputes, all loaded/);
    expect(router.state.location.search).toEqual({ status: "Accepted" });
    expect(api.queueRequests.at(-1)?.searchParams.get("status")).toBe("Accepted");
    expect(within(screen.getByRole("table")).queryByText("Submitted")).not.toBeInTheDocument();
  });

  it("reads the status filter from the URL", async () => {
    await openQueue("/agent?status=UnderReview");
    expect(screen.getByLabelText("Status")).toHaveValue("UnderReview");
  });

  it("pages with the cursor, oldest first, and never repeats a row", async () => {
    const { api } = await openQueue();
    expect(rows()).toBe(25);

    await userEvent.setup().click(screen.getByRole("button", { name: "Load more" }));

    await screen.findByText(/Showing 40 disputes, all loaded/);
    expect(api.queueRequests.at(-1)?.searchParams.get("cursor")).toBeTruthy();
    expect(rows()).toBe(40);
    expect(screen.queryByRole("button", { name: "Load more" })).not.toBeInTheDocument();
    const merchants = within(screen.getByRole("table"))
      .getAllByRole("rowheader")
      .map((cell) => cell.textContent);
    expect(new Set(merchants).size).toBe(40);
  });

  it("says so when the queue is empty", async () => {
    server.use(signedIn(bob), ...fakeAgentApi([]).handlers);
    renderApp("/agent");
    expect(await screen.findByText("Nothing is waiting for a decision")).toBeInTheDocument();
  });

  it("shows a retryable error when the queue cannot load", async () => {
    server.use(
      signedIn(bob),
      http.get("/api/v1/agent/disputes", () => problem(500)),
    );
    renderApp("/agent");
    expect(
      await screen.findByRole("heading", { name: "We could not load the review queue" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
  });
});

describe("agent queue: status links", () => {
  const filterLinks = () => within(screen.getByRole("navigation", { name: "Filter by status" }));

  it("offers Open and the five statuses, with exactly one marked current", async () => {
    await openQueue("/agent?status=Accepted");
    const links = filterLinks().getAllByRole("link");

    expect(links.map((link) => link.textContent)).toEqual([
      "Open",
      "Submitted",
      "Under review",
      "Accepted",
      "Rejected",
      "Withdrawn",
    ]);
    expect(links.map((link) => link.getAttribute("href"))).toEqual([
      "/agent",
      "/agent?status=Submitted",
      "/agent?status=UnderReview",
      "/agent?status=Accepted",
      "/agent?status=Rejected",
      "/agent?status=Withdrawn",
    ]);
    expect(links.filter((link) => link.getAttribute("aria-current") === "page")).toEqual([
      filterLinks().getByRole("link", { name: "Accepted" }),
    ]);
    expect(filterLinks().queryByRole("tab")).not.toBeInTheDocument();
  });

  it("Open drops the status and keeps the limit; Open is current by default", async () => {
    const { router } = await openQueue("/agent?status=Accepted&limit=10");
    expect(filterLinks().getByRole("link", { name: "Open" })).toHaveAttribute(
      "href",
      "/agent?limit=10",
    );
    expect(filterLinks().getByRole("link", { name: "Withdrawn" })).toHaveAttribute(
      "href",
      "/agent?limit=10&status=Withdrawn",
    );

    await userEvent.setup().click(filterLinks().getByRole("link", { name: "Open" }));

    await waitFor(() => expect(router.state.location.search).toEqual({ limit: 10 }));
    expect(filterLinks().getByRole("link", { name: "Open" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(filterLinks().getByRole("link", { name: "Accepted" })).not.toHaveAttribute(
      "aria-current",
    );
  });
});

describe("agent queue: progress", () => {
  it("gives each row an icon-only stepper named by its step, and Review buttons keep their names", async () => {
    await openQueue();
    const [first, second] = within(screen.getByRole("table")).getAllByRole("row").slice(1);
    expect(within(first as HTMLElement).getByRole("img")).toHaveAccessibleName("Step 1 of 3");
    expect(within(second as HTMLElement).getByRole("img")).toHaveAccessibleName("Step 2 of 3");
    expect(within(first as HTMLElement).getByRole("img")).toHaveTextContent("");
    expect(
      within(first as HTMLElement).getByRole("link", {
        name: "Review dispute for Merchant 0001 from Customer 1",
      }),
    ).toBeInTheDocument();
  });
});
