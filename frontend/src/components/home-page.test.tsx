import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { fakeAgentApi } from "@/test/agent";
import { alice, bob, carol, problem, signedIn } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";
import { fakeTransactionsApi, makeTransactions } from "@/test/transactions";

describe("home page", () => {
  beforeEach(() => {
    // Only the clock is faked, so the greeting is deterministic and MSW/timers keep working.
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date(2026, 10, 15, 9, 30));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("greets the customer by first name for the time of day and shows the status legend", async () => {
    server.use(signedIn(alice), fakeTransactionsApi([]).handler);
    renderApp("/");
    expect(
      await screen.findByRole("heading", { level: 1, name: "Good morning, Alice" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "How a dispute moves" })).toBeInTheDocument();
    await screen.findByText("You have no transactions yet"); // let the list's request finish
  });

  it.each([
    ["an agent", bob],
    ["a user with both roles", carol],
  ])("does not show the customer status legend to %s", async (_who, user) => {
    server.use(signedIn(user), ...fakeAgentApi([]).handlers);
    renderApp("/");
    await screen.findByRole("heading", { level: 1 });
    expect(screen.queryByRole("heading", { name: "How a dispute moves" })).not.toBeInTheDocument();
  });

  it("does not show a customer their role names", async () => {
    server.use(signedIn(alice), fakeTransactionsApi([]).handler);
    renderApp("/");
    await screen.findByRole("heading", { level: 1 });

    expect(screen.queryByText("Roles")).not.toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Roles" })).not.toBeInTheDocument();
    expect(screen.queryByText("customer")).not.toBeInTheDocument();
    await screen.findByText("You have no transactions yet");
  });

  it("shows a session with both roles its role names, and asks for no customer's data", async () => {
    const { api, handler } = fakeTransactionsApi(makeTransactions(3));
    server.use(signedIn(carol), handler);
    renderApp("/");

    const list = within(await screen.findByRole("list", { name: "Roles" }));
    expect(list.getAllByRole("listitem").map((item) => item.textContent)).toEqual([
      "customer",
      "dispute-agent",
    ]);
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(api.requests).toEqual([]);
    expect(screen.queryByRole("heading", { name: "Recent transactions" })).not.toBeInTheDocument();
  });

  it("gives an agent the workbench: no role names and no customer's data", async () => {
    const transactions = fakeTransactionsApi(makeTransactions(3));
    server.use(signedIn(bob), transactions.handler, ...fakeAgentApi([]).handlers);
    renderApp("/");

    expect(
      await screen.findByRole("heading", { level: 1, name: "Good morning, Bob" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Take next" })).toBeInTheDocument();
    expect(screen.queryByText("Roles")).not.toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Roles" })).not.toBeInTheDocument();
    expect(screen.queryByText("dispute-agent")).not.toBeInTheDocument();
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(transactions.api.requests).toEqual([]);
    expect(screen.queryByRole("heading", { name: "Recent transactions" })).not.toBeInTheDocument();
  });
});

describe("home page: recent transactions", () => {
  const seeAll = () => screen.getByRole("link", { name: "See all transactions and filters" });
  const rowCount = () => within(screen.getByRole("table")).getAllByRole("row").length - 1;

  it("lists the newest transactions below the cards, with a way on to all of them", async () => {
    const { api, handler } = fakeTransactionsApi(makeTransactions(30));
    server.use(signedIn(alice), handler);
    renderApp("/");

    const table = within(await screen.findByRole("table"));
    expect(table.getAllByRole("row")).toHaveLength(10 + 1); // a short first page, plus the header row
    expect(table.getByRole("rowheader", { name: /Merchant 0001/ })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Recent transactions" })).toBeVisible();
    expect(api.requests).toHaveLength(1);
    expect(Object.fromEntries(api.requests[0]!.searchParams)).toEqual({ limit: "10" });
    expect(seeAll()).toHaveAttribute("href", "/transactions");
    // Under the cards, not among them: the card list is still just the two of them.
    expect(
      within(screen.getByRole("navigation", { name: "Main" })).getAllByRole("link"),
    ).toHaveLength(2);
  });

  it("keeps the page's one crimson action: the My transactions card", async () => {
    server.use(signedIn(alice), fakeTransactionsApi(makeTransactions(3)).handler);
    renderApp("/");
    await screen.findByRole("table");

    const crimson = document.querySelectorAll(".bg-primary");
    expect(crimson).toHaveLength(1);
    expect(crimson[0]).toContainElement(screen.getByRole("link", { name: "My transactions" }));
  });

  it("keeps /transactions on its own list: pages loaded on Home do not leak into it", async () => {
    const { api, handler } = fakeTransactionsApi(makeTransactions(60));
    server.use(signedIn(alice), handler);
    renderApp("/");
    const user = userEvent.setup();
    await screen.findByRole("table");
    await user.click(screen.getByRole("button", { name: "Load more" }));
    await waitFor(() => expect(rowCount()).toBe(20));

    await user.click(seeAll());

    expect(await screen.findByRole("heading", { level: 1, name: "Transactions" })).toBeVisible();
    await waitFor(() => expect(rowCount()).toBe(25)); // its own default page, not Home's 20
    expect(screen.getByText("Showing 25 transactions")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Recent transactions" })).not.toBeInTheDocument();
    expect(
      api.requests.map((url) => [url.searchParams.get("limit"), url.searchParams.has("cursor")]),
    ).toEqual([
      ["10", false], // Home's first page
      ["10", true], // Home's Load more
      ["25", false], // /transactions asks for its own first page
    ]);
  });

  it("teaches the next step when the customer has no transactions", async () => {
    server.use(signedIn(alice), fakeTransactionsApi([]).handler);
    renderApp("/");

    expect(await screen.findByText("You have no transactions yet")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    expect(seeAll()).toBeInTheDocument();
  });

  it("shows the problem and its reference when the list fails, and the rest of Home still works", async () => {
    server.use(
      signedIn(alice),
      http.get("/api/v1/transactions", () =>
        problem(503, { code: "idp_unavailable", traceId: "trace-home-503" }),
      ),
    );
    renderApp("/");

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("We could not load your transactions");
    expect(alert).toHaveTextContent("Reference: trace-home-503");
    expect(screen.getByRole("link", { name: "My transactions" })).toHaveAttribute(
      "href",
      "/transactions",
    );
    expect(screen.getByRole("heading", { name: "How a dispute moves" })).toBeInTheDocument();
  });

  it("shows a loading state, announced once, while the first page is in flight", async () => {
    server.use(
      signedIn(alice),
      http.get("/api/v1/transactions", async () => {
        await new Promise<never>(() => undefined);
        return HttpResponse.json({});
      }),
    );
    renderApp("/");

    const summary = await screen.findByRole("status", { name: "Result count" });
    expect(summary).toHaveTextContent("Loading transactions");
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
});
