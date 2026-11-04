import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";

import { problem, signedIn } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";
import { fakeTransactionsApi, makeTransactions } from "@/test/transactions";

/** Serve `count` transactions through the fake API and render /transactions at `path`. */
async function openTransactions(path = "/transactions", count = 150) {
  const { api, handler } = fakeTransactionsApi(makeTransactions(count));
  server.use(signedIn(), handler);
  const view = renderApp(path);
  await screen.findByRole("heading", { name: "Transactions" });
  return { api, ...view };
}

const rowCount = () => within(screen.getByRole("table")).getAllByRole("row").length - 1;
const setDate = (label: "From" | "To", value: string) =>
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
const input = (label: "From" | "To" | "Status") => screen.getByLabelText(label);
const lastRequest = (requests: URL[]) => Object.fromEntries(requests.at(-1)!.searchParams);

describe("transactions page: list and paging", () => {
  it("requests the newest page with the default limit and no cursor", async () => {
    const { api } = await openTransactions();
    await screen.findByRole("table");

    expect(api.requests).toHaveLength(1);
    expect(lastRequest(api.requests)).toEqual({ limit: "25" });
    expect(rowCount()).toBe(25);
    expect(screen.getByText("Showing 25 transactions")).toBeInTheDocument();
    expect(document.title).toBe("Transactions - Transactions Dispute Portal");
  });

  it("pages through 150 transactions with no duplicates or gaps, using the cursor it was given", async () => {
    const { api } = await openTransactions();
    await screen.findByRole("table");
    const user = userEvent.setup();

    for (let page = 2; page <= 6; page++) {
      await user.click(screen.getByRole("button", { name: "Load more" }));
      await waitFor(() => expect(rowCount()).toBe(page * 25));
      expect(screen.getByText(new RegExp(`Showing ${page * 25} transactions`))).toBeInTheDocument();
    }

    // Every reference once, in newest-first order: 0001..0150.
    const references = within(screen.getByRole("table"))
      .getAllByRole("rowheader")
      .map((header) => /REF-(\d+)/.exec(header.textContent)?.[1]);
    expect(references).toEqual(
      Array.from({ length: 150 }, (_, i) => String(i + 1).padStart(4, "0")),
    );

    // Page N+1 was requested with page N's cursor (and only that), never a made-up one.
    const cursors = api.requests.map((url) => url.searchParams.get("cursor"));
    expect(cursors[0]).toBeNull();
    expect(
      cursors.slice(1).every((cursor) => typeof cursor === "string" && cursor.length > 0),
    ).toBe(true);
    expect(new Set(cursors).size).toBe(cursors.length);
    // The opaque cursor is never shown.
    expect(document.body.textContent).not.toContain(cursors[1]);

    // After the last page: no button, a closing announcement, and focus has not been dropped.
    expect(screen.queryByRole("button", { name: "Load more" })).not.toBeInTheDocument();
    const status = screen.getByText("Showing 150 transactions, all loaded");
    expect(status).toHaveAttribute("role", "status");
    expect(status).toHaveFocus();
  });

  it("announces the loading state and then the new count in one live region", async () => {
    await openTransactions();
    await screen.findByRole("table");
    const status = screen.getByText("Showing 25 transactions");

    await userEvent.setup().click(screen.getByRole("button", { name: "Load more" }));

    // Same element throughout, so assistive tech announces the change.
    await waitFor(() => expect(status).toHaveTextContent("Showing 50 transactions"));
    expect(screen.getAllByRole("status").includes(status)).toBe(true);
  });

  it("never renders a row twice even if the server repeats one across pages", async () => {
    const all = makeTransactions(30);
    server.use(
      signedIn(),
      http.get("/api/v1/transactions", ({ request }) => {
        const cursor = new URL(request.url).searchParams.get("cursor");
        return cursor
          ? HttpResponse.json({ items: all.slice(20, 30), nextCursor: null }) // repeats 21..25
          : HttpResponse.json({ items: all.slice(0, 25), nextCursor: "next" });
      }),
    );
    renderApp("/transactions");
    await screen.findByRole("table");

    await userEvent.setup().click(screen.getByRole("button", { name: "Load more" }));

    await waitFor(() => expect(screen.getByText(/all loaded/)).toBeInTheDocument());
    expect(rowCount()).toBe(30);
  });

  it("shows loading skeletons while the first page is in flight", async () => {
    server.use(
      signedIn(),
      http.get("/api/v1/transactions", async () => {
        await new Promise<never>(() => undefined);
        return HttpResponse.json({});
      }),
    );
    renderApp("/transactions");

    expect(await screen.findByText("Loading transactions...")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("says so when there are no transactions at all", async () => {
    server.use(signedIn(), fakeTransactionsApi([]).handler);
    renderApp("/transactions");
    expect(await screen.findByText("You have no transactions yet")).toBeInTheDocument();
    expect(screen.queryByText("No transactions match these filters")).not.toBeInTheDocument();
  });

  it("says no transactions match when filters are applied", async () => {
    server.use(signedIn(), fakeTransactionsApi(makeTransactions(10)).handler);
    renderApp("/transactions?status=Reversed");
    expect(await screen.findByText("No transactions match these filters")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Load more" })).not.toBeInTheDocument();
  });

  it("links to Home from the header", async () => {
    await openTransactions();
    expect(screen.getByRole("link", { name: "Transactions Dispute Portal" })).toHaveAttribute(
      "href",
      "/",
    );
  });
});

describe("transactions page: filters from the URL", () => {
  it("sends validated URL params to the API and shows them in the form", async () => {
    const { api } = await openTransactions(
      "/transactions?from=2026-10-01&to=2026-10-31&status=Posted&limit=10",
    );
    await screen.findByRole("table");

    expect(lastRequest(api.requests)).toEqual({
      limit: "10",
      from: "2026-10-01",
      to: "2026-10-31",
      status: "Posted",
    });
    expect(input("From")).toHaveValue("2026-10-01");
    expect(input("To")).toHaveValue("2026-10-31");
    expect(input("Status")).toHaveValue("Posted");
    expect(rowCount()).toBe(10);
  });

  it.each([
    ["limit 0", "?limit=0", { limit: "25" }],
    ["limit 101", "?limit=101", { limit: "25" }],
    ["limit not a number", "?limit=abc", { limit: "25" }],
    ["limit fractional", "?limit=2.5", { limit: "25" }],
    ["limit 100 is fine", "?limit=100", { limit: "100" }],
    ["date that does not exist", "?from=2026-02-30", { limit: "25" }],
    ["garbage from, good to", "?from=banana&to=2026-10-01", { limit: "25", to: "2026-10-01" }],
    ["compact date", "?from=20261001", { limit: "25" }],
    ["unknown status", "?status=Hacked", { limit: "25" }],
    ["lowercase status", "?status=posted", { limit: "25" }],
    [
      "from after to drops both",
      "?from=2026-10-31&to=2026-10-01&status=Posted",
      { limit: "25", status: "Posted" },
    ],
    ["367 days drops both", "?from=2025-01-01&to=2026-01-03", { limit: "25" }],
    [
      "366 days is kept",
      "?from=2025-01-01&to=2026-01-02",
      { limit: "25", from: "2025-01-01", to: "2026-01-02" },
    ],
    ["a cursor in the URL is ignored", "?cursor=abc", { limit: "25" }],
  ] as [string, string, Record<string, string>][])(
    "falls back silently for an invalid URL: %s",
    async (_name, query, expected) => {
      const { api } = await openTransactions(`/transactions${query}`);
      await waitFor(() => expect(api.requests.length).toBeGreaterThan(0));

      expect(lastRequest(api.requests)).toEqual(expected);
      // Never a crash and never a server-side 400: the page renders normally.
      expect(screen.getByRole("heading", { name: "Transactions" })).toBeInTheDocument();
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
      // The form shows what is actually applied, not the junk.
      expect(input("From")).toHaveValue(expected["from"] ?? "");
      expect(input("Status")).toHaveValue(expected["status"] ?? "");
    },
  );
});

describe("transactions page: applying filters", () => {
  it("writes valid values to the URL, refetches, and Back restores the previous view", async () => {
    const { api, router } = await openTransactions();
    await screen.findByRole("table");
    const user = userEvent.setup();

    setDate("From", "2026-10-01");
    setDate("To", "2026-10-31");
    await user.selectOptions(input("Status"), "Posted");
    await user.click(screen.getByRole("button", { name: "Apply filters" }));

    await waitFor(() =>
      expect(router.state.location.search).toEqual({
        from: "2026-10-01",
        to: "2026-10-31",
        status: "Posted",
      }),
    );
    // 31 posted transactions fall in October; the first page holds 25 and offers the rest.
    await waitFor(() => {
      const dates = [...screen.getByRole("table").querySelectorAll("time")].map((t) => t.dateTime);
      expect(dates).toHaveLength(25);
      expect(dates.every((date) => date.startsWith("2026-10-"))).toBe(true);
    });
    expect(rowCount()).toBe(25);
    expect(screen.getByRole("button", { name: "Load more" })).toBeInTheDocument();
    expect(lastRequest(api.requests)).toEqual({
      limit: "25",
      from: "2026-10-01",
      to: "2026-10-31",
      status: "Posted",
    });
    // The default limit is not written to the URL.
    expect(router.state.location.search).not.toHaveProperty("limit");

    act(() => router.history.back());

    await waitFor(() => expect(router.state.location.search).toEqual({}));
    await waitFor(() => expect(input("From")).toHaveValue(""));
    expect(input("To")).toHaveValue("");
    expect(input("Status")).toHaveValue("");
    await waitFor(() => expect(rowCount()).toBe(25));
  });

  it("keeps a non-default limit when filters change", async () => {
    const { router } = await openTransactions("/transactions?limit=10");
    await screen.findByRole("table");

    await userEvent.setup().selectOptions(input("Status"), "Posted");
    await userEvent.setup().click(screen.getByRole("button", { name: "Apply filters" }));

    await waitFor(() =>
      expect(router.state.location.search).toEqual({ status: "Posted", limit: 10 }),
    );
  });

  it("applies with Enter from a date field", async () => {
    const { router } = await openTransactions();
    await screen.findByRole("table");
    const user = userEvent.setup();

    setDate("From", "2026-10-01");
    await user.click(input("From"));
    await user.keyboard("{Enter}");

    await waitFor(() => expect(router.state.location.search).toEqual({ from: "2026-10-01" }));
  });

  it("Clear filters empties the form and the URL and shows the unfiltered list", async () => {
    const { api, router } = await openTransactions(
      "/transactions?from=2026-10-01&to=2026-10-31&status=Posted",
    );
    await screen.findByRole("table");

    await userEvent.setup().click(screen.getByRole("button", { name: "Clear filters" }));

    await waitFor(() => expect(router.state.location.search).toEqual({}));
    expect(input("From")).toHaveValue("");
    expect(input("To")).toHaveValue("");
    expect(input("Status")).toHaveValue("");
    await waitFor(() => expect(lastRequest(api.requests)).toEqual({ limit: "25" }));
    await waitFor(() => expect(rowCount()).toBe(25));
  });

  it("Clear filters also discards edits that were never applied", async () => {
    const { api } = await openTransactions();
    await screen.findByRole("table");
    const before = api.requests.length;

    setDate("From", "2026-10-31");
    setDate("To", "2026-10-01");
    await userEvent.setup().click(screen.getByRole("button", { name: "Clear filters" }));

    expect(input("From")).toHaveValue("");
    expect(input("To")).toHaveValue("");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(api.requests).toHaveLength(before);
  });
});

describe("transactions page: filter form validation", () => {
  it("shows no error while typing, then validates on blur, then on every change", async () => {
    await openTransactions();
    await screen.findByRole("table");
    const user = userEvent.setup();

    setDate("From", "2026-10-10");
    setDate("To", "2026-10-01");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument(); // not touched yet

    await user.click(input("To"));
    await user.tab(); // blur "To"
    const error = await screen.findByRole("alert");
    expect(error).toHaveTextContent("The end date must be on or after the start date");
    expect(input("To")).toHaveAttribute("aria-invalid", "true");
    expect(input("To")).toHaveAccessibleDescription(
      "The end date must be on or after the start date",
    );
    expect(input("From")).not.toHaveAttribute("aria-invalid");

    // Fixing the OTHER end clears it: the rule spans both fields.
    setDate("From", "2026-09-01");
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
    expect(input("To")).not.toHaveAttribute("aria-invalid");

    // And re-breaking it is caught immediately, no second blur needed.
    setDate("To", "2026-08-01");
    expect(await screen.findByRole("alert")).toHaveTextContent("on or after the start date");
  });

  it("rejects a range over 366 days and accepts exactly 366", async () => {
    await openTransactions();
    await screen.findByRole("table");
    const user = userEvent.setup();

    setDate("From", "2025-01-01");
    setDate("To", "2026-01-03");
    await user.click(input("To"));
    await user.tab();
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Choose a range of 366 days or less",
    );

    setDate("To", "2026-01-02");
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
  });

  it("sends nothing and moves focus to the invalid field when applying an invalid form", async () => {
    const { api, router } = await openTransactions();
    await screen.findByRole("table");
    const requestsBefore = api.requests.length;
    const user = userEvent.setup();

    setDate("From", "2026-10-31");
    setDate("To", "2026-10-01");
    await user.click(screen.getByRole("button", { name: "Apply filters" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("on or after the start date");
    expect(input("To")).toHaveFocus();
    expect(api.requests).toHaveLength(requestsBefore);
    expect(router.state.location.search).toEqual({});
    // The list still shows the last valid result.
    expect(rowCount()).toBe(25);
  });

  it("focuses the FIRST invalid field when several are invalid", async () => {
    const { api } = await openTransactions();
    await screen.findByRole("table");
    const before = api.requests.length;
    // A half-typed native date reports value "" but validity.badInput.
    for (const label of ["From", "To"] as const) {
      Object.defineProperty(input(label), "validity", {
        configurable: true,
        value: { badInput: true },
      });
    }

    await userEvent.setup().click(screen.getByRole("button", { name: "Apply filters" }));

    const alerts = await screen.findAllByRole("alert");
    expect(alerts.map((alert) => alert.textContent)).toEqual([
      "Enter a complete date",
      "Enter a complete date",
    ]);
    expect(input("From")).toHaveFocus();
    expect(input("From")).toHaveAttribute("aria-invalid", "true");
    expect(input("To")).toHaveAttribute("aria-invalid", "true");
    expect(api.requests).toHaveLength(before);
  });

  it("flags a half-typed date when the field loses focus", async () => {
    await openTransactions();
    await screen.findByRole("table");
    Object.defineProperty(input("From"), "validity", {
      configurable: true,
      value: { badInput: true },
    });

    await userEvent.setup().click(input("From"));
    await userEvent.setup().tab();

    expect(await screen.findByRole("alert")).toHaveTextContent("Enter a complete date");
  });

  it("applies once the form becomes valid again", async () => {
    const { api, router } = await openTransactions();
    await screen.findByRole("table");
    const user = userEvent.setup();

    setDate("From", "2026-10-31");
    setDate("To", "2026-10-01");
    await user.click(screen.getByRole("button", { name: "Apply filters" }));
    await screen.findByRole("alert");

    setDate("From", "2026-10-01");
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Apply filters" }));

    await waitFor(() =>
      expect(router.state.location.search).toEqual({ from: "2026-10-01", to: "2026-10-01" }),
    );
    await waitFor(() =>
      expect(lastRequest(api.requests)).toEqual({
        limit: "25",
        from: "2026-10-01",
        to: "2026-10-01",
      }),
    );
  });

  it("labels every control", async () => {
    await openTransactions();
    expect(screen.getByRole("form", { name: "Filter transactions" })).toBeInTheDocument();
    expect(input("From")).toHaveAttribute("type", "date");
    expect(input("To")).toHaveAttribute("type", "date");
    expect(input("Status").tagName).toBe("SELECT");
    expect(
      within(input("Status"))
        .getAllByRole("option")
        .map((o) => o.textContent),
    ).toEqual(["All statuses", "Pending", "Posted", "Reversed"]);
  });
});

describe("transactions page: errors", () => {
  it("shows the problem's message with its trace reference and recovers on retry", async () => {
    let fail = true;
    const { handler } = fakeTransactionsApi(makeTransactions(5), () =>
      fail
        ? problem(500, {
            title: "Internal Server Error",
            detail: "Something broke on our side.",
            traceId: "trace-500",
          })
        : undefined,
    );
    server.use(signedIn(), handler);
    renderApp("/transactions");

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("We could not load your transactions");
    expect(alert).toHaveTextContent("Something broke on our side.");
    expect(alert).toHaveTextContent("Reference: trace-500");

    fail = false;
    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));

    await screen.findByRole("table");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("treats 503 idp_unavailable as retryable, never as 'sign in again'", async () => {
    let up = false;
    const { handler } = fakeTransactionsApi(makeTransactions(5), () =>
      up
        ? undefined
        : problem(503, {
            title: "Service Unavailable",
            detail: "The identity provider is unavailable. Please try again shortly.",
            code: "idp_unavailable",
            traceId: "trace-idp",
          }),
    );
    server.use(signedIn(), handler);
    renderApp("/transactions");

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("The identity provider is unavailable");
    expect(alert).toHaveTextContent("Reference: trace-idp");
    expect(screen.queryByRole("heading", { name: "Sign in" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Logout" })).toBeInTheDocument(); // still signed in

    up = true;
    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));
    await screen.findByRole("table");
  });

  it("goes to the Sign in screen when the API says the session ended (401)", async () => {
    server.use(
      signedIn(),
      http.get("/api/v1/transactions", () =>
        problem(401, { title: "Unauthorized", code: "unauthenticated" }),
      ),
    );
    renderApp("/transactions");
    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
  });

  it("shows a server-side 400 validation failure with its reference (a safety net)", async () => {
    server.use(
      signedIn(),
      http.get("/api/v1/transactions", () =>
        problem(400, {
          title: "Validation failed",
          detail: "limit must be 1..100",
          code: "validation_failed",
          traceId: "trace-400",
        }),
      ),
    );
    renderApp("/transactions");
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("limit must be 1..100");
    expect(alert).toHaveTextContent("Reference: trace-400");
  });

  it("keeps the loaded rows when only the next page fails, and retries that page", async () => {
    let failNext = true;
    const all = makeTransactions(50);
    server.use(
      signedIn(),
      http.get("/api/v1/transactions", ({ request }) => {
        const cursor = new URL(request.url).searchParams.get("cursor");
        if (!cursor) return HttpResponse.json({ items: all.slice(0, 25), nextCursor: "p2" });
        return failNext
          ? problem(500, { traceId: "trace-next", detail: "Page two failed." })
          : HttpResponse.json({ items: all.slice(25), nextCursor: null });
      }),
    );
    renderApp("/transactions");
    await screen.findByRole("table");

    await userEvent.setup().click(screen.getByRole("button", { name: "Load more" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("We could not load more transactions");
    expect(alert).toHaveTextContent("Reference: trace-next");
    expect(rowCount()).toBe(25);

    failNext = false;
    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(rowCount()).toBe(50));
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});

describe("transactions page: invalid_cursor", () => {
  /** Page 1 always works; any cursor is rejected. Records the cursor of every request. */
  function rejectEveryCursor() {
    const cursors: (string | null)[] = [];
    server.use(
      signedIn(),
      http.get("/api/v1/transactions", ({ request }) => {
        const cursor = new URL(request.url).searchParams.get("cursor");
        cursors.push(cursor);
        if (cursor) {
          return problem(400, {
            title: "Bad Request",
            detail: "The cursor is not valid.",
            code: "invalid_cursor",
            traceId: "trace-cursor",
          });
        }
        return HttpResponse.json({ items: makeTransactions(25), nextCursor: "page-two" });
      }),
    );
    return cursors;
  }

  it("restarts from the first page once, with a gentle message", async () => {
    const cursors = rejectEveryCursor();
    renderApp("/transactions");
    await screen.findByRole("table");

    await userEvent.setup().click(screen.getByRole("button", { name: "Load more" }));

    expect(
      await screen.findByText("Your list was out of date, so we reloaded it from the start."),
    ).toBeInTheDocument();
    expect(cursors).toEqual([null, "page-two", null]);
    expect(rowCount()).toBe(25);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Load more" })).toBeInTheDocument();
  });

  it("does not loop: a second invalid cursor is shown as an error with a way to start over", async () => {
    const cursors = rejectEveryCursor();
    renderApp("/transactions");
    await screen.findByRole("table");
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Load more" }));
    await screen.findByText(/we reloaded it from the start/);
    await user.click(screen.getByRole("button", { name: "Load more" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("The cursor is not valid.");
    expect(alert).toHaveTextContent("Reference: trace-cursor");
    // Settled: exactly [page 1, bad cursor, page 1, bad cursor] and nothing more.
    await new Promise((resolve) => setTimeout(resolve, 100));
    expect(cursors).toEqual([null, "page-two", null, "page-two"]);

    // "Try again" starts over rather than replaying the same bad cursor.
    await user.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(cursors).toEqual([null, "page-two", null, "page-two", null]));
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
  });
});
