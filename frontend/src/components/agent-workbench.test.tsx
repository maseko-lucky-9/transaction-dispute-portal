import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";

import { agentKeys, type AgentDisputeDetail } from "@/api/agent";
import { fakeAgentApi, makeAgentDispute, OTHER_AGENT } from "@/test/agent";
import { bob, problem, signedIn } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";

const MINE = "dddddddd-0000-4000-8000-000000000001";
const UNCLAIMED = "dddddddd-0000-4000-8000-000000000002";
const OTHERS = "dddddddd-0000-4000-8000-000000000003";

/** One dispute per section: index 0 is mine, 1 is unclaimed (the oldest Submitted), 2 a colleague's. */
const oneEach = (): AgentDisputeDetail[] => [
  makeAgentDispute({ status: "UnderReview" }, 0),
  makeAgentDispute({ status: "Submitted" }, 1),
  makeAgentDispute({ status: "UnderReview", assigneeId: OTHER_AGENT }, 2),
];

const SECTIONS = ["In review by you", "Unclaimed", "Claimed by other agents"] as const;
const section = (name: (typeof SECTIONS)[number]) => screen.getByRole("region", { name });
const takeNext = () => screen.getByRole("button", { name: /^(Take next|Taking\.\.\.)$/ });

async function openWorkbench(disputes: AgentDisputeDetail[] = oneEach()) {
  const fake = fakeAgentApi(disputes);
  server.use(signedIn(bob), ...fake.handlers);
  const view = renderApp("/");
  await screen.findByRole("button", { name: "Take next" });
  return { ...fake, ...view };
}

/** Wait until no section is still loading. */
async function settled() {
  await waitFor(() =>
    expect(
      screen.queryAllByRole("status", { name: "Result count" }).map((s) => s.textContent),
    ).not.toContainEqual(expect.stringMatching(/^Loading/)),
  );
}

describe("agent workbench: the sections", () => {
  it("asks for each section by assignment, never with a status, and lists its disputes", async () => {
    const { api } = await openWorkbench();
    await within(section("Claimed by other agents")).findByText("Merchant 0003");

    const asked = api.queueRequests.map((url) => Object.fromEntries(url.searchParams));
    expect(asked).toEqual(
      expect.arrayContaining([
        { assignment: "mine", limit: "10" },
        { assignment: "unclaimed", limit: "10" },
        { assignment: "others", limit: "10" },
      ]),
    );
    expect(asked.every((params) => !("status" in params))).toBe(true);
    expect(within(section("In review by you")).getByText("Merchant 0001")).toBeInTheDocument();
    expect(within(section("Unclaimed")).getByText("Merchant 0002")).toBeInTheDocument();
    expect(within(section("Unclaimed")).queryByText("Merchant 0001")).not.toBeInTheDocument();
  });

  it("teaches what to do when every section is empty", async () => {
    await openWorkbench([]);

    expect(
      await screen.findByText(
        "Nothing is waiting. New disputes appear here the moment a customer submits one.",
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText("You are not reviewing a dispute. Take next, or open one from Unclaimed."),
    ).toBeInTheDocument();
    expect(
      screen.getByText("No other agent is reviewing a dispute right now."),
    ).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it.each([
    ["mine", "In review by you", "We could not load the disputes you are reviewing"],
    ["unclaimed", "Unclaimed", "We could not load the unclaimed disputes"],
    [
      "others",
      "Claimed by other agents",
      "We could not load the disputes other agents are reviewing",
    ],
  ] as const)(
    "shows a retryable error in %s alone when it cannot load",
    async (assignment, name, title) => {
      const fake = fakeAgentApi(oneEach());
      server.use(
        signedIn(bob),
        http.get("/api/v1/agent/disputes", ({ request }) =>
          new URL(request.url).searchParams.get("assignment") === assignment
            ? problem(503, { code: "idp_unavailable", traceId: "trace-wb-503" })
            : undefined,
        ),
        ...fake.handlers,
      );
      renderApp("/");

      const alert = await within(await screen.findByRole("region", { name })).findByRole("alert");
      expect(alert).toHaveTextContent(title);
      expect(alert).toHaveTextContent("Reference: trace-wb-503");
      expect(within(alert).getByRole("button", { name: "Try again" })).toBeInTheDocument();
      // The other sections still load.
      for (const other of SECTIONS.filter((s) => s !== name)) {
        expect(await within(section(other)).findByRole("table")).toBeInTheDocument();
      }
    },
  );

  it("shows no counts or totals: no digits in any heading or in the lists' summaries", async () => {
    // More than a page in Unclaimed, so its summary says there is more.
    const many = Array.from({ length: 12 }, (_, i) =>
      makeAgentDispute({ status: "Submitted" }, i + 3),
    );
    await openWorkbench([...oneEach(), ...many]);
    await within(section("Claimed by other agents")).findByRole("table");
    await settled();

    const headings = screen.getAllByRole("heading").map((h) => h.textContent ?? "");
    expect(headings).toEqual(expect.arrayContaining(["Next dispute", ...SECTIONS]));
    for (const heading of headings) expect(heading).not.toMatch(/\d/);
    const summaries = screen.getAllByRole("status", { name: "Result count" });
    expect(summaries.map((s) => s.textContent)).toEqual([
      "Your disputes: all loaded",
      "Unclaimed disputes: more to load",
      "Other agents' disputes: all loaded",
    ]);
    for (const summary of summaries) expect(summary.textContent).not.toMatch(/\d/);
    expect(document.body.textContent).not.toMatch(/Showing \d/);
  });

  it("has exactly one crimson control: Take next", async () => {
    await openWorkbench();
    await within(section("Claimed by other agents")).findByRole("table");
    await settled();

    const crimson = document.querySelectorAll(".bg-primary");
    expect(crimson).toHaveLength(1);
    expect(crimson[0]).toBe(takeNext());
  });

  it("other agents' disputes are read only: an outlined Read link, the claim age, nothing to act on", async () => {
    await openWorkbench();
    const others = within(section("Claimed by other agents"));
    await others.findByRole("table");

    const read = others.getByRole("link", {
      name: "Read dispute for Merchant 0003 from Customer 3",
    });
    expect(read).toHaveAttribute("href", `/agent/disputes/${OTHERS}`);
    expect(read).toHaveClass("border");
    expect(read).not.toHaveClass("bg-primary");
    expect(others.queryByRole("link", { name: /^(Review|Start)/ })).not.toBeInTheDocument();
    expect(others.queryByRole("button")).not.toBeInTheDocument();
    expect(others.getByText(/^Claimed (today|yesterday|\d+ days ago)$/)).toBeInTheDocument();
    // My own section links to Review and says when I claimed it; Unclaimed has no claim column.
    const mine = within(section("In review by you"));
    expect(mine.getByRole("link", { name: /^Review dispute for Merchant 0001/ })).toHaveAttribute(
      "href",
      `/agent/disputes/${MINE}`,
    );
    expect(mine.getByText(/^Claimed /)).toBeInTheDocument();
    expect(
      within(section("Unclaimed")).queryByRole("columnheader", { name: "Claimed" }),
    ).not.toBeInTheDocument();
  });
});

describe("agent workbench: take next", () => {
  it("claims the oldest unclaimed dispute, seeds its page from the answer and opens it", async () => {
    const { router, queryClient, api } = await openWorkbench();
    // The page must open on the claim itself: a read of the dispute would never answer.
    server.use(
      http.get(`/api/v1/agent/disputes/${UNCLAIMED}`, () => new Promise<never>(() => undefined)),
    );

    await userEvent.setup().click(takeNext());

    expect(
      await screen.findByRole("heading", { level: 1, name: /^Dispute: Merchant 0002/ }),
    ).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/agent/disputes/${UNCLAIMED}`);
    expect(screen.getByText("In review by you")).toBeInTheDocument();
    expect(queryClient.getQueryData(agentKeys.detail(UNCLAIMED))).toMatchObject({
      etag: '"v2"',
      dispute: { id: UNCLAIMED, status: "UnderReview", assignment: "You" },
    });
    expect(api.takeNext.calls).toBe(1);
  });

  it("says nothing is waiting when there is nothing to take (204), and stays on Home", async () => {
    const { router, api } = await openWorkbench([makeAgentDispute({ status: "UnderReview" })]);

    await userEvent.setup().click(takeNext());

    const message = await screen.findByText("Nothing is waiting for review right now.");
    expect(message).toHaveAttribute("role", "status");
    expect(router.state.location.pathname).toBe("/");
    expect(api.takeNext.calls).toBe(1);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("claims once on a double click, and is disabled while it is taking", async () => {
    let calls = 0;
    let finish: () => void = () => undefined;
    const fake = fakeAgentApi(oneEach());
    server.use(
      signedIn(bob),
      http.post("/api/v1/agent/disputes/take-next", async () => {
        calls += 1;
        await new Promise<void>((resolve) => {
          finish = resolve;
        });
        return new HttpResponse(null, { status: 204 });
      }),
      ...fake.handlers,
    );
    renderApp("/");
    const user = userEvent.setup();

    await user.dblClick(await screen.findByRole("button", { name: "Take next" }));
    expect(takeNext()).toHaveAttribute("aria-disabled", "true");
    await user.click(takeNext());
    expect(calls).toBe(1);

    finish();
    expect(await screen.findByText("Nothing is waiting for review right now.")).toBeInTheDocument();
    expect(takeNext()).not.toHaveAttribute("aria-disabled");
    expect(calls).toBe(1);
  });

  it("shows the error with its reference and tries again", async () => {
    let calls = 0;
    const fake = fakeAgentApi(oneEach());
    server.use(
      signedIn(bob),
      http.post("/api/v1/agent/disputes/take-next", () => {
        calls += 1;
        return problem(503, { code: "idp_unavailable", traceId: "trace-take-503" });
      }),
      ...fake.handlers,
    );
    renderApp("/");
    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Take next" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("We could not take the next dispute");
    expect(alert).toHaveTextContent("Reference: trace-take-503");
    await user.click(within(alert).getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(calls).toBe(2));
  });

  it.each([404, 405])(
    "against an older api with no take next (%i) it says so plainly",
    async (status) => {
      const fake = fakeAgentApi(oneEach());
      server.use(
        signedIn(bob),
        http.post("/api/v1/agent/disputes/take-next", () => problem(status, { code: "not_found" })),
        ...fake.handlers,
      );
      renderApp("/");
      await userEvent.setup().click(await screen.findByRole("button", { name: "Take next" }));

      expect(
        await screen.findByText(
          "Take next isn't available right now. Pick a dispute from Unclaimed instead.",
        ),
      ).toHaveAttribute("role", "status");
      expect(screen.queryByText("We could not take the next dispute")).not.toBeInTheDocument();
    },
  );
});

describe("agent workbench: an older api with no assignment", () => {
  /** What an api from before assignment answers to any queue request: the open queue, no fields. */
  const legacyItem = (d: AgentDisputeDetail) => ({
    id: d.id,
    status: d.status,
    createdAt: d.createdAt,
    merchantName: d.transaction.merchantName,
    amount: d.amount,
    currency: d.currency,
    reasonCode: d.reasonCode,
    customerDisplayName: d.customerDisplayName,
    maskedAccountNumber: d.maskedAccountNumber,
  });

  function legacyQueue(disputes: AgentDisputeDetail[]) {
    return http.get("/api/v1/agent/disputes", () =>
      HttpResponse.json({
        items: disputes.map((d) => ({
          id: d.id,
          status: d.status,
          createdAt: d.createdAt,
          merchantName: d.transaction.merchantName,
          amount: d.amount,
          currency: d.currency,
          reasonCode: d.reasonCode,
          customerDisplayName: d.customerDisplayName,
          maskedAccountNumber: d.maskedAccountNumber,
        })),
        nextCursor: null,
      }),
    );
  }

  it("keeps Unclaimed to the Submitted rows and does not guess who reviews the rest", async () => {
    const disputes = oneEach();
    server.use(signedIn(bob), legacyQueue(disputes), ...fakeAgentApi(disputes).handlers);
    renderApp("/");

    const unclaimed = within(await screen.findByRole("region", { name: "Unclaimed" }));
    expect(await unclaimed.findByText("Merchant 0002")).toBeInTheDocument();
    expect(unclaimed.queryByText("Merchant 0001")).not.toBeInTheDocument();
    expect(unclaimed.queryByText("Merchant 0003")).not.toBeInTheDocument();
    for (const name of ["In review by you", "Claimed by other agents"] as const) {
      const region = within(section(name));
      expect(
        await region.findByText(/We cannot show who is reviewing what yet\./),
      ).toBeInTheDocument();
      expect(region.getByRole("link", { name: "The review queue" })).toHaveAttribute(
        "href",
        "/agent",
      );
      expect(region.queryByRole("table")).not.toBeInTheDocument();
    }
    expect(document.body.textContent).not.toMatch(/undefined|null/);
  });

  it("does not call Unclaimed empty while another page may hold a Submitted row", async () => {
    // Page one of the open queue is all under review; the Submitted row is on page two.
    const reviews = Array.from({ length: 2 }, (_, i) =>
      makeAgentDispute({ status: "UnderReview" }, i),
    );
    const waiting = makeAgentDispute({ status: "Submitted" }, 2);
    server.use(
      signedIn(bob),
      http.get("/api/v1/agent/disputes", ({ request }) =>
        new URL(request.url).searchParams.get("cursor") === "page-2"
          ? HttpResponse.json({ items: [legacyItem(waiting)], nextCursor: null })
          : HttpResponse.json({ items: reviews.map(legacyItem), nextCursor: "page-2" }),
      ),
      ...fakeAgentApi([...reviews, waiting]).handlers,
    );
    renderApp("/");
    const unclaimed = within(await screen.findByRole("region", { name: "Unclaimed" }));

    expect(
      await unclaimed.findByText("None of these are waiting. Load more to look further back."),
    ).toBeInTheDocument();
    expect(
      unclaimed.queryByText(
        "Nothing is waiting. New disputes appear here the moment a customer submits one.",
      ),
    ).not.toBeInTheDocument();
    await userEvent.setup().click(unclaimed.getByRole("button", { name: "Load more" }));

    expect(await unclaimed.findByText("Merchant 0003")).toBeInTheDocument();
    expect(unclaimed.queryByText("Merchant 0001")).not.toBeInTheDocument();
    expect(unclaimed.queryByRole("button", { name: "Load more" })).not.toBeInTheDocument();
  });
});

describe("agent workbench: a legacy review with no assignee", () => {
  it("stays under other agents but links with Review: any agent may decide it", async () => {
    await openWorkbench([
      makeAgentDispute({ status: "UnderReview", assigneeId: OTHER_AGENT }, 0),
      makeAgentDispute({ status: "UnderReview", assigneeId: null }, 1),
    ]);
    const others = within(section("Claimed by other agents"));
    await others.findByText("Merchant 0002");

    expect(
      others.getByRole("link", { name: "Read dispute for Merchant 0001 from Customer 1" }),
    ).toBeInTheDocument();
    expect(
      others.getByRole("link", { name: "Review dispute for Merchant 0002 from Customer 2" }),
    ).toHaveAttribute("href", "/agent/disputes/dddddddd-0000-4000-8000-000000000002");
    expect(document.body.textContent).not.toMatch(/undefined|null/);
  });
});
