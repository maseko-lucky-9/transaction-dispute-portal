import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { delay, http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";

import type * as Navigation from "@/lib/navigation";
import { navigate } from "@/lib/navigation";
import { alice, problem, signedIn, signedOut } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";
import { fakeTransactionsApi, makeTransactions } from "@/test/transactions";

vi.mock("@/lib/navigation", async (importOriginal) => ({
  ...(await importOriginal<typeof Navigation>()),
  navigate: vi.fn(),
}));

describe("session gate: signed in (200)", () => {
  it("shows the user, the roles as badges and the shell", async () => {
    server.use(signedIn({ ...alice, roles: ["customer", "dispute_agent"] }));
    renderApp();

    expect(await screen.findByText(/Signed in as/)).toHaveTextContent(
      "Signed in as Alice Customer",
    );
    const roles = within(screen.getByRole("list", { name: "Roles" }));
    expect(roles.getByText("customer")).toBeInTheDocument();
    expect(roles.getByText("dispute_agent")).toBeInTheDocument();

    expect(screen.getByText("Transactions Dispute Portal")).toBeInTheDocument();
    expect(screen.getByTestId("header-user")).toHaveTextContent("Alice Customer");
    expect(screen.getByRole("button", { name: "Logout" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Sign in" })).not.toBeInTheDocument();
  });

  it("says so when the user has no roles", async () => {
    server.use(signedIn({ ...alice, roles: [] }));
    renderApp();
    expect(await screen.findByText("none assigned")).toBeInTheDocument();
  });

  it("links My transactions and My disputes for a customer, and shows no agent queue", async () => {
    server.use(signedIn());
    renderApp();
    await screen.findByText(/Signed in as/);

    const transactions = screen.getByRole("link", { name: "My transactions" });
    expect(transactions).toHaveAttribute("href", "/transactions");
    expect(transactions).not.toHaveAttribute("aria-disabled");
    expect(screen.getByRole("link", { name: "My disputes" })).toHaveAttribute("href", "/disputes");
    expect(screen.queryByText("Agent queue")).not.toBeInTheDocument();
    expect(screen.queryByText("Coming soon")).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /queue/i })).not.toBeInTheDocument();
  });

  it("opens the transactions page from Home", async () => {
    const { handler } = fakeTransactionsApi(makeTransactions(3));
    server.use(signedIn(), handler);
    const { router } = renderApp();
    await screen.findByText(/Signed in as/);

    await userEvent.setup().click(screen.getByRole("link", { name: "My transactions" }));

    expect(await screen.findByRole("heading", { name: "Transactions" })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/transactions");
    await screen.findByRole("table");
  });

  it("calls /bff/user exactly once on load", async () => {
    let calls = 0;
    server.use(
      http.get("/bff/user", () => {
        calls += 1;
        return HttpResponse.json(alice);
      }),
    );
    renderApp();
    await screen.findByText(/Signed in as/);
    expect(calls).toBe(1);
  });
});

describe("session gate: signed out (401)", () => {
  it("shows the Sign in screen and no app content", async () => {
    server.use(signedOut());
    renderApp();

    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
    expect(screen.queryByText(/Signed in as/)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Logout" })).not.toBeInTheDocument();
  });

  it("signs in with a top-level navigation carrying the current path, never a fetch", async () => {
    server.use(signedOut());
    window.history.replaceState({}, "", "/transactions?status=open");
    renderApp();
    const fetchSpy = vi.spyOn(globalThis, "fetch");

    await userEvent.setup().click(await screen.findByRole("button", { name: "Sign in" }));

    expect(navigate).toHaveBeenCalledExactlyOnceWith(
      "/bff/login?returnUrl=%2Ftransactions%3Fstatus%3Dopen",
    );
    const fetched = fetchSpy.mock.calls.map(([input]) =>
      input instanceof Request ? input.url : input.toString(),
    );
    expect(fetched.some((url) => url.includes("/bff/login"))).toBe(false);
  });
});

describe("session gate: loading and failure", () => {
  it("shows a busy status while the session is being checked", async () => {
    server.use(
      http.get("/bff/user", async () => {
        await delay("infinite");
        return HttpResponse.json(alice);
      }),
    );
    renderApp();

    expect(await screen.findByRole("status")).toHaveAttribute("aria-busy", "true");
    expect(screen.queryByRole("heading", { name: "Sign in" })).not.toBeInTheDocument();
  });

  it("shows the error with its trace reference, and recovers on retry", async () => {
    server.use(
      http.get("/bff/user", () =>
        problem(503, { title: "Service Unavailable", traceId: "trace-abc" }),
      ),
    );
    renderApp();

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("We could not check your session");
    // The page has no other content, so the error carries the page heading.
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent(
      "We could not check your session",
    );
    expect(alert).toHaveTextContent("Reference: trace-abc");
    expect(screen.queryByRole("heading", { name: "Sign in" })).not.toBeInTheDocument();

    server.use(signedIn());
    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByText(/Signed in as/)).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("returns to the Sign in screen when any later call reports the session ended", async () => {
    server.use(signedIn());
    const { queryClient } = renderApp();
    await screen.findByText(/Signed in as/);

    server.use(http.get("/api/v1/transactions", () => problem(401, { title: "Unauthorized" })));
    const { apiFetch } = await import("@/api/client");
    await queryClient
      .fetchQuery({
        queryKey: ["transactions"],
        queryFn: () => apiFetch("/api/v1/transactions"),
        retry: false,
      })
      .catch(() => undefined);

    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
    expect(screen.queryByText(/Signed in as/)).not.toBeInTheDocument();
  });
});

describe("logout", () => {
  it("clears the query cache, then navigates to the server-supplied logoutUrl", async () => {
    server.use(signedIn());
    const { queryClient } = renderApp();
    await screen.findByText(/Signed in as/);
    queryClient.setQueryData(["transactions"], [{ id: "t1" }]);

    let cachedWhenNavigating = -1;
    vi.mocked(navigate).mockImplementation(() => {
      cachedWhenNavigating = queryClient.getQueryCache().getAll().length;
    });

    await userEvent.setup().click(screen.getByRole("button", { name: "Logout" }));

    await waitFor(() => expect(navigate).toHaveBeenCalledOnce());
    expect(navigate).toHaveBeenCalledWith("/bff/logout?sid=abc123");
    expect(cachedWhenNavigating).toBe(0);
  });
});
