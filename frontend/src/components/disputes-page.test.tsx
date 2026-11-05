import { act, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";

import type { DisputeStatus } from "@/api/disputes";
import { makeDisputeDetail, fakeDisputesApi } from "@/test/disputes";
import { problem, signedIn } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";

const STATUS_CYCLE: DisputeStatus[] = [
  "Submitted",
  "UnderReview",
  "Accepted",
  "Rejected",
  "Withdrawn",
];

function manyDisputes(count: number) {
  return Array.from({ length: count }, (_, index) =>
    makeDisputeDetail(
      { status: STATUS_CYCLE[index % STATUS_CYCLE.length] as DisputeStatus },
      index,
    ),
  );
}

async function openDisputes(path = "/disputes", count = 60) {
  const { api, handlers } = fakeDisputesApi({ disputes: manyDisputes(count) });
  server.use(signedIn(), ...handlers);
  const view = renderApp(path);
  await screen.findByRole("heading", { name: "My disputes" });
  return { api, ...view };
}

const rowCount = () => within(screen.getByRole("table")).getAllByRole("row").length - 1;
const statusSelect = () => screen.getByLabelText("Status");
const lastRequest = (requests: URL[]) => Object.fromEntries(requests.at(-1)?.searchParams ?? []);
const listRequests = (requests: URL[]) =>
  requests.filter((url) => url.pathname === "/api/v1/disputes");

describe("disputes history: list", () => {
  it("loads the newest page with the default limit and shows each dispute's facts", async () => {
    const { api } = await openDisputes();
    await screen.findByRole("table");

    expect(lastRequest(listRequests(api.requests))).toEqual({ limit: "25" });
    expect(rowCount()).toBe(25);
    expect(screen.getByRole("table", { name: "Your disputes, newest first" })).toBeInTheDocument();
    expect(screen.getByText("Showing 25 disputes")).toBeInTheDocument();
    expect(document.title).toBe("My disputes - Transactions Dispute Portal");

    const first = within(screen.getAllByRole("row")[1] as HTMLElement);
    expect(first.getByText("Merchant 0001")).toBeInTheDocument();
    expect(first.getByText(/^R\s123,45$/)).toBeInTheDocument();
    expect(first.getByText("Submitted")).toBeInTheDocument();
    expect(first.getByRole("link", { name: /^View dispute for Merchant 0001/ })).toHaveAttribute(
      "href",
      "/disputes/dddddddd-0000-4000-8000-000000000001",
    );
  });

  it("shows ALL statuses (the historic view), each as a word", async () => {
    await openDisputes();
    await screen.findByRole("table");
    const table = within(screen.getByRole("table"));
    for (const word of ["Submitted", "Under review", "Accepted", "Rejected", "Withdrawn"]) {
      expect(table.getAllByText(word).length).toBeGreaterThan(0);
    }
  });

  it("uses the reason catalogue's labels", async () => {
    await openDisputes();
    await screen.findByRole("table");
    expect(
      within(screen.getByRole("table")).getAllByText("I did not make this purchase"),
    ).not.toHaveLength(0);
  });

  it("falls back to a readable reason when the catalogue is unavailable", async () => {
    const { handlers } = fakeDisputesApi({
      disputes: [makeDisputeDetail({ reasonCode: "not_received" })],
    });
    server.use(
      signedIn(),
      http.get("/api/v1/dispute-reasons", () => problem(500)),
      ...handlers.filter((_, index) => index !== 0),
    );
    renderApp("/disputes");
    await screen.findByRole("table");
    expect(await screen.findByText("Not received")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("pages through everything with no duplicates, echoing only the cursor it was given", async () => {
    const { api } = await openDisputes();
    await screen.findByRole("table");
    const user = userEvent.setup();

    for (let page = 2; page <= 3; page++) {
      await user.click(screen.getByRole("button", { name: "Load more" }));
      await waitFor(() => expect(rowCount()).toBe(Math.min(page * 25, 60)));
    }

    const merchants = within(screen.getByRole("table"))
      .getAllByRole("rowheader")
      .map((h) => h.textContent);
    expect(merchants).toEqual(
      Array.from({ length: 60 }, (_, i) => `Merchant ${String(i + 1).padStart(4, "0")}`),
    );
    expect(screen.queryByRole("button", { name: "Load more" })).not.toBeInTheDocument();
    expect(screen.getByText("Showing 60 disputes, all loaded")).toHaveFocus();
    const cursors = listRequests(api.requests).map((url) => url.searchParams.get("cursor"));
    expect(cursors[0]).toBeNull();
    expect(new Set(cursors).size).toBe(cursors.length);
  });

  it("links to Home-level navigation: header has Disputes marked as the current page", async () => {
    await openDisputes();
    const nav = screen.getByRole("navigation", { name: "Primary" });
    expect(within(nav).getByRole("link", { name: "Disputes" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(within(nav).getByRole("link", { name: "Transactions" })).not.toHaveAttribute(
      "aria-current",
    );
  });
});

describe("disputes history: status filter and URL", () => {
  it("lists every status in the select, 'All statuses' first", async () => {
    await openDisputes();
    expect(
      within(statusSelect())
        .getAllByRole("option")
        .map((o) => o.textContent),
    ).toEqual(["All statuses", "Submitted", "Under review", "Accepted", "Rejected", "Withdrawn"]);
  });

  it("choosing a status writes it to the URL, filters the list, and Back restores", async () => {
    const { api, router } = await openDisputes();
    await screen.findByRole("table");

    await userEvent.setup().selectOptions(statusSelect(), "UnderReview");

    await waitFor(() => expect(router.state.location.search).toEqual({ status: "UnderReview" }));
    await waitFor(() =>
      expect(lastRequest(listRequests(api.requests))).toEqual({
        limit: "25",
        status: "UnderReview",
      }),
    );
    await waitFor(() => expect(rowCount()).toBe(12));
    const badges = within(screen.getByRole("table")).getAllByText("Under review");
    expect(badges).toHaveLength(12);
    expect(statusSelect()).toHaveValue("UnderReview");

    act(() => router.history.back());
    await waitFor(() => expect(statusSelect()).toHaveValue(""));
    await waitFor(() => expect(rowCount()).toBe(25));
  });

  it("'All statuses' clears the filter from the URL", async () => {
    const { router } = await openDisputes("/disputes?status=Accepted");
    await screen.findByRole("table");
    expect(statusSelect()).toHaveValue("Accepted");

    await userEvent.setup().selectOptions(statusSelect(), "");

    await waitFor(() => expect(router.state.location.search).toEqual({}));
  });

  it("keeps a non-default limit when the status changes", async () => {
    const { router } = await openDisputes("/disputes?limit=10");
    await screen.findByRole("table");
    await userEvent.setup().selectOptions(statusSelect(), "Rejected");
    await waitFor(() =>
      expect(router.state.location.search).toEqual({ status: "Rejected", limit: 10 }),
    );
  });

  it.each([
    ["unknown status", "?status=Hacked", { limit: "25" }],
    ["lowercase status", "?status=accepted", { limit: "25" }],
    ["limit 0", "?limit=0", { limit: "25" }],
    ["limit 101", "?limit=101", { limit: "25" }],
    ["limit not a number", "?limit=abc", { limit: "25" }],
    ["valid status and limit", "?status=Accepted&limit=10", { limit: "10", status: "Accepted" }],
    ["a cursor in the URL is ignored", "?cursor=abc", { limit: "25" }],
  ] as [string, string, Record<string, string>][])(
    "falls back silently for an invalid URL: %s",
    async (_name, query, expected) => {
      const { api } = await openDisputes(`/disputes${query}`);
      await waitFor(() => expect(listRequests(api.requests).length).toBeGreaterThan(0));

      expect(lastRequest(listRequests(api.requests))).toEqual(expected);
      expect(screen.getByRole("heading", { name: "My disputes" })).toBeInTheDocument();
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
      expect(statusSelect()).toHaveValue(expected["status"] ?? "");
    },
  );
});

describe("disputes history: empty and error states", () => {
  it("invites the customer to dispute a transaction when there is no history", async () => {
    server.use(signedIn(), ...fakeDisputesApi().handlers);
    renderApp("/disputes");
    expect(await screen.findByText("You have not raised any disputes yet")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "your transactions" })).toHaveAttribute(
      "href",
      "/transactions",
    );
    expect(screen.getByRole("status", { name: "Result count" })).toHaveTextContent(
      "Showing 0 disputes",
    );
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("says which status is empty when a filter hides everything", async () => {
    server.use(signedIn(), ...fakeDisputesApi({ disputes: [makeDisputeDetail()] }).handlers);
    renderApp("/disputes?status=UnderReview");
    expect(await screen.findByText("No under review disputes")).toBeInTheDocument();
    expect(screen.getByText("Choose another status, or all statuses.")).toBeInTheDocument();
  });

  it("shows a server error with its trace reference, and recovers on retry", async () => {
    let fail = true;
    const { handlers } = fakeDisputesApi({ disputes: manyDisputes(3) });
    server.use(
      signedIn(),
      http.get("/api/v1/disputes", (info) => {
        if (!fail) return undefined;
        void info;
        return problem(500, { detail: "Something broke on our side.", traceId: "trace-disputes" });
      }),
      ...handlers,
    );
    renderApp("/disputes");

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("We could not load your disputes");
    expect(alert).toHaveTextContent("Reference: trace-disputes");

    fail = false;
    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));
    await screen.findByRole("table");
    expect(screen.getByRole("status", { name: "Result count" })).toHaveFocus();
  });

  it("keeps the rows and offers a retry when only the next page fails", async () => {
    let fail = true;
    const all = manyDisputes(30);
    server.use(
      signedIn(),
      http.get("/api/v1/dispute-reasons", () => HttpResponse.json([])),
      http.get("/api/v1/disputes", ({ request }) => {
        const cursor = new URL(request.url).searchParams.get("cursor");
        if (!cursor) return HttpResponse.json({ items: all.slice(0, 25), nextCursor: "p2" });
        return fail
          ? problem(503, { detail: "Page two failed.", traceId: "trace-p2" })
          : HttpResponse.json({ items: all.slice(25), nextCursor: null });
      }),
    );
    renderApp("/disputes");
    await screen.findByRole("table");
    const loadMore = screen.getByRole("button", { name: "Load more" });

    await userEvent.setup().click(loadMore);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("We could not load more disputes");
    expect(alert).toHaveTextContent("Reference: trace-p2");
    expect(rowCount()).toBe(25);
    expect(loadMore).toHaveFocus();

    fail = false;
    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(rowCount()).toBe(30));
  });

  it("goes to Sign in when the API says the session ended", async () => {
    server.use(
      signedIn(),
      http.get("/api/v1/dispute-reasons", () => HttpResponse.json([])),
      http.get("/api/v1/disputes", () => problem(401, { code: "unauthenticated" })),
    );
    renderApp("/disputes");
    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
  });
});
