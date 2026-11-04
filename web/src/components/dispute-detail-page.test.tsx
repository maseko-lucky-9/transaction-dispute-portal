import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";

import type { DisputeDetail } from "@/api/disputes";
import { fakeDisputesApi, makeDisputeDetail } from "@/test/disputes";
import { problem, signedIn } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";

async function openDispute(
  detail: DisputeDetail = makeDisputeDetail({ merchantName: "Woolworths Sandton" }),
  options: Parameters<typeof fakeDisputesApi>[0] = {},
) {
  const fake = fakeDisputesApi({ disputes: [detail], ...options });
  server.use(signedIn(), ...fake.handlers);
  const view = renderApp(`/disputes/${detail.id}`);
  await screen.findByRole("heading", { level: 1, name: /^Dispute:/ });
  return { ...fake, detail, ...view };
}

const statusRegion = () =>
  within(screen.getByRole("main"))
    .getAllByRole("status")
    .find((el) => el.hasAttribute("tabindex")) as HTMLElement;
const withdrawSection = () => screen.getByRole("region", { name: "Withdraw" });
const timelineItems = () =>
  within(screen.getByRole("list", { name: "Timeline" })).getAllByRole("listitem");

describe("dispute detail: content", () => {
  it("shows the header, amounts, reason, description and the transaction", async () => {
    await openDispute(
      makeDisputeDetail({
        merchantName: "Woolworths Sandton",
        amount: "50.00",
        description: "Charged for goods I never received.\nCalled the shop twice.",
        reasonCode: "not_received",
        transaction: {
          id: "00000000-0000-4000-8000-0000000000aa",
          reference: "REF-4242",
          merchantName: "Woolworths Sandton",
          amount: "123.45",
          currency: "ZAR",
          transactionDate: "2026-10-30T08:15:00Z",
        },
      }),
    );

    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent(
      "Dispute: Woolworths Sandton",
    );
    expect(screen.getAllByText("Submitted").length).toBeGreaterThan(0);
    expect(screen.getByText("Amount disputed").nextElementSibling).toHaveTextContent(/^R\s50,00$/);
    expect(await screen.findByText("I never received the goods or service")).toBeInTheDocument();
    const description = screen.getByText(/Charged for goods I never received\./);
    expect(description).toHaveTextContent("Called the shop twice.");
    expect(description.className).toContain("whitespace-pre-wrap");

    const tx = within(screen.getByRole("region", { name: "The transaction" }));
    expect(tx.getByText("REF-4242")).toBeInTheDocument();
    expect(tx.getByText("Amount").nextElementSibling).toHaveTextContent(/^R\s123,45$/);
    expect(tx.getByText("30 Oct 2026")).toBeInTheDocument();
    expect(document.title).toBe("Dispute - Transactions Dispute Portal");
  });

  it("shows the resolution note and the closing time once decided", async () => {
    await openDispute(
      makeDisputeDetail({
        status: "Rejected",
        resolutionNote: "The merchant provided proof of delivery.",
        resolvedAt: "2026-11-06T12:00:00Z",
      }),
    );
    expect(screen.getByText("Resolution note")).toBeInTheDocument();
    expect(screen.getByText("The merchant provided proof of delivery.")).toBeInTheDocument();
    expect(screen.getByText("Closed").nextElementSibling).toHaveTextContent(
      "6 Nov 2026, 12:00 UTC",
    );
  });

  it("shows neither a resolution note nor a closing time while it is open", async () => {
    await openDispute();
    expect(screen.queryByText("Resolution note")).not.toBeInTheDocument();
    expect(screen.queryByText("Closed")).not.toBeInTheDocument();
  });

  it("renders the customer's own text as text, never markup", async () => {
    await openDispute(
      makeDisputeDetail({ description: "<img src=x onerror=alert(1)> <b>bold</b>" }),
    );
    expect(document.querySelector("img")).toBeNull();
    expect(document.querySelector("main b")).toBeNull();
    expect(screen.getByText(/<img src=x onerror=alert\(1\)>/)).toBeInTheDocument();
  });

  it("shows the whole timeline, with 'You' and 'Back-office', and never an id", async () => {
    const base = makeDisputeDetail({ merchantName: "Netflix" });
    const { api, detail } = await openDispute({
      ...base,
      status: "UnderReview",
      timeline: [
        ...base.timeline,
        {
          from: "Submitted",
          to: "UnderReview",
          actorType: "Agent",
          note: "Looking into it.",
          occurredAt: "2026-11-05T08:00:00Z",
        },
      ],
    });

    expect(timelineItems()).toHaveLength(2);
    expect(timelineItems()[0]).toHaveTextContent("You submitted this dispute");
    expect(timelineItems()[1]).toHaveTextContent(
      "Back-office changed the status from Submitted to Under review",
    );
    expect(timelineItems()[1]).toHaveTextContent("Note: Looking into it.");

    const text = document.body.textContent;
    expect(text).not.toContain(detail.id);
    expect(text).not.toContain(detail.transactionId);
    expect(api.requests.length).toBeGreaterThan(0);
  });

  it("links back to the history", async () => {
    await openDispute();
    expect(screen.getByRole("link", { name: "Back to my disputes" })).toHaveAttribute(
      "href",
      "/disputes",
    );
  });
});

describe("dispute detail: not found, malformed and failing", () => {
  it("shows a friendly page for an unknown id (404), with a way back", async () => {
    server.use(signedIn(), ...fakeDisputesApi().handlers);
    renderApp("/disputes/aaaaaaaa-0000-4000-8000-000000000999");
    expect(await screen.findByRole("heading", { name: "Dispute not found" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back to my disputes" })).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it.each(["not-a-guid", "123", "aaaaaaaa-0000-4000-8000-00000000099", "../transactions"])(
    "does not even ask the API about the malformed id %j",
    async (id) => {
      const { api, handlers } = fakeDisputesApi();
      server.use(signedIn(), ...handlers);
      renderApp(`/disputes/${encodeURIComponent(id)}`);
      expect(await screen.findByRole("heading", { name: "Dispute not found" })).toBeInTheDocument();
      expect(api.requests).toHaveLength(0);
    },
  );

  it("shows a server error with its trace reference and recovers on retry", async () => {
    let fail = true;
    const detail = makeDisputeDetail({ merchantName: "Uber" });
    const fake = fakeDisputesApi({
      disputes: [detail],
      interceptGet: () =>
        fail ? problem(503, { detail: "Try later.", traceId: "trace-detail" }) : undefined,
    });
    server.use(signedIn(), ...fake.handlers);
    renderApp(`/disputes/${detail.id}`);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("We could not load this dispute");
    expect(alert).toHaveTextContent("Reference: trace-detail");
    expect(screen.getByRole("heading", { level: 1 })).toBeInTheDocument();

    fail = false;
    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));
    expect(
      await screen.findByRole("heading", { level: 1, name: "Dispute: Uber" }),
    ).toBeInTheDocument();
  });
});

describe("dispute detail: withdraw", () => {
  it("offers Withdraw for Submitted and Under review, and explains when it no longer can", async () => {
    await openDispute(makeDisputeDetail({ status: "UnderReview" }));
    expect(screen.getByRole("button", { name: "Withdraw dispute" })).toBeInTheDocument();
  });

  it.each(["Accepted", "Rejected", "Withdrawn"] as const)(
    "offers no Withdraw for a %s dispute",
    async (status) => {
      await openDispute(makeDisputeDetail({ status }));
      expect(screen.queryByRole("button", { name: "Withdraw dispute" })).not.toBeInTheDocument();
      expect(within(withdrawSection()).getByText(/can no longer be withdrawn/)).toBeInTheDocument();
    },
  );

  it("asks first: the confirmation is part of the page, focused on its question, and sends nothing yet", async () => {
    const { api } = await openDispute();
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Withdraw dispute" }));

    const group = screen.getByRole("group", { name: "Withdraw this dispute?" });
    expect(within(group).getByText("Withdraw this dispute?")).toHaveFocus();
    expect(within(group).getByRole("button", { name: "Yes, withdraw" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Withdraw dispute" })).not.toBeInTheDocument();
    expect(api.withdrawCalls).toHaveLength(0);
  });

  it("'Keep dispute' cancels, returns focus to Withdraw, and sends nothing", async () => {
    const { api } = await openDispute();
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Withdraw dispute" }));

    await user.click(screen.getByRole("button", { name: "Keep dispute" }));

    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Withdraw dispute" })).toHaveFocus(),
    );
    expect(screen.queryByRole("group")).not.toBeInTheDocument();
    expect(api.withdrawCalls).toHaveLength(0);
  });

  it("Escape cancels too", async () => {
    const { api } = await openDispute();
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Withdraw dispute" }));

    await user.keyboard("{Escape}");

    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Withdraw dispute" })).toHaveFocus(),
    );
    expect(api.withdrawCalls).toHaveLength(0);
  });

  it("withdraws with the ETag it read (If-Match), shows the result and moves focus to the status", async () => {
    const { api, detail } = await openDispute();
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Withdraw dispute" }));
    await user.click(screen.getByRole("button", { name: "Yes, withdraw" }));

    await waitFor(() => expect(statusRegion()).toHaveTextContent("Dispute withdrawn."));
    expect(api.withdrawCalls).toEqual([{ id: detail.id, ifMatch: '"v1"' }]);
    expect(statusRegion()).toHaveFocus();
    expect(screen.getAllByText("Withdrawn").length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: "Withdraw dispute" })).not.toBeInTheDocument();
    expect(screen.queryByRole("group")).not.toBeInTheDocument();
    await waitFor(() => expect(timelineItems()).toHaveLength(2));
    expect(timelineItems()[1]).toHaveTextContent("You withdrew this dispute");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("after withdrawing, the history and the transactions list show the new state", async () => {
    const { detail, api, router } = await openDispute();
    const user = userEvent.setup();
    // Warm the history cache first, so "refetched" is observable.
    await act(async () => {
      await router.navigate({ to: "/disputes" });
    });
    await screen.findByRole("table");
    await act(async () => {
      await router.navigate({ to: "/disputes/$disputeId", params: { disputeId: detail.id } });
    });
    await screen.findByRole("heading", { level: 1, name: /^Dispute:/ });
    const before = api.requests.filter((u) => u.pathname === "/api/v1/disputes").length;

    await user.click(screen.getByRole("button", { name: "Withdraw dispute" }));
    await user.click(screen.getByRole("button", { name: "Yes, withdraw" }));
    await waitFor(() => expect(statusRegion()).toHaveTextContent("Dispute withdrawn."));

    await user.click(screen.getByRole("link", { name: "Back to my disputes" }));
    await screen.findByRole("table");
    expect(within(screen.getByRole("table")).getByText("Withdrawn")).toBeInTheDocument();
    expect(api.requests.filter((u) => u.pathname === "/api/v1/disputes").length).toBeGreaterThan(
      before,
    );
  });

  it("ignores a second activation while the request is in flight", async () => {
    const { api } = await openDispute(makeDisputeDetail(), {
      interceptWithdraw: async () => {
        await new Promise((resolve) => setTimeout(resolve, 100));
        return undefined;
      },
    });
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Withdraw dispute" }));
    await user.dblClick(screen.getByRole("button", { name: "Yes, withdraw" }));

    await waitFor(() => expect(statusRegion()).toHaveTextContent("Dispute withdrawn."));
    expect(api.withdrawCalls).toHaveLength(1);
  });
});

describe("dispute detail: withdraw double activation", () => {
  it("sends one request when Yes, withdraw is activated twice in the same instant", async () => {
    const { api } = await openDispute(makeDisputeDetail(), {
      interceptWithdraw: async () => {
        await new Promise((resolve) => setTimeout(resolve, 50));
        return undefined;
      },
    });
    await userEvent.setup().click(screen.getByRole("button", { name: "Withdraw dispute" }));

    const confirm = screen.getByRole("button", { name: "Yes, withdraw" });
    fireEvent.click(confirm);
    fireEvent.click(confirm); // before React has rendered "pending": the button still looks live

    await waitFor(() => expect(statusRegion()).toHaveTextContent("Dispute withdrawn."));
    expect(api.withdrawCalls).toHaveLength(1);
  });

  it("sends one request when Try again is activated twice in the same instant", async () => {
    let fail = true;
    const { api } = await openDispute(makeDisputeDetail(), {
      interceptWithdraw: async () => {
        await new Promise((resolve) => setTimeout(resolve, 50));
        return fail ? problem(503, { traceId: "trace-503" }) : undefined;
      },
    });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Withdraw dispute" }));
    await user.click(screen.getByRole("button", { name: "Yes, withdraw" }));
    await screen.findByRole("alert");
    expect(api.withdrawCalls).toHaveLength(1);

    fail = false;
    const retry = screen.getByRole("button", { name: "Try again" });
    fireEvent.click(retry);
    fireEvent.click(retry);

    await waitFor(() => expect(statusRegion()).toHaveTextContent("Dispute withdrawn."));
    expect(api.withdrawCalls).toHaveLength(2); // the first failure, and exactly one retry
  });
});

describe("dispute detail: withdraw failures", () => {
  it("412: says the dispute changed, offers Reload, and Reload shows the latest and works again", async () => {
    const { api, detail } = await openDispute();
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Withdraw dispute" }));
    // An agent moves the dispute on while the confirmation is open: our ETag is now stale.
    api.agentActs(detail.id, "UnderReview", "Looking into it.");

    await user.click(screen.getByRole("button", { name: "Yes, withdraw" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("This dispute changed. Reload to see the latest.");
    expect(alert).toHaveTextContent("Reference:");
    expect(api.withdrawCalls).toEqual([{ id: detail.id, ifMatch: '"v1"' }]);
    expect(screen.queryByRole("button", { name: "Yes, withdraw" })).not.toBeInTheDocument();
    expect(screen.getAllByText("Submitted").length).toBeGreaterThan(0); // still the stale view

    await user.click(screen.getByRole("button", { name: "Reload" }));

    await waitFor(() =>
      expect(statusRegion()).toHaveTextContent("Showing the latest version of this dispute."),
    );
    expect(statusRegion()).toHaveFocus();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(timelineItems()).toHaveLength(2);
    expect(timelineItems()[1]).toHaveTextContent("Note: Looking into it.");

    // With the fresh ETag the same action now succeeds.
    await user.click(screen.getByRole("button", { name: "Withdraw dispute" }));
    await user.click(screen.getByRole("button", { name: "Yes, withdraw" }));
    await waitFor(() => expect(statusRegion()).toHaveTextContent("Dispute withdrawn."));
    expect(api.withdrawCalls.at(-1)).toEqual({ id: detail.id, ifMatch: '"v2"' });
  });

  it("409 invalid_transition: says it is already closed and shows what is true now", async () => {
    const { api, detail } = await openDispute();
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Withdraw dispute" }));
    // The agent accepted it, and the server answers 409 even to a current ETag.
    server.use(
      http.post("/api/v1/disputes/:id/withdraw", () => {
        api.agentActs(detail.id, "Accepted", "Refunded.");
        return problem(409, {
          code: "invalid_transition",
          detail: "An Accepted dispute cannot be withdrawn.",
        });
      }),
    );

    await user.click(screen.getByRole("button", { name: "Yes, withdraw" }));

    await waitFor(() =>
      expect(statusRegion()).toHaveTextContent("This dispute was already closed."),
    );
    expect(statusRegion()).toHaveFocus();
    // Refetched: the page now shows the decision, and no Withdraw is offered.
    expect(await screen.findByText("Resolution note")).toBeInTheDocument();
    expect(screen.getAllByText("Accepted").length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: "Withdraw dispute" })).not.toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("428 cannot happen from this page: if the server says it anyway it is a failure with its reference, not a stale-data hint", async () => {
    await openDispute(makeDisputeDetail(), {
      interceptWithdraw: () =>
        problem(428, {
          title: "Precondition Required",
          code: "precondition_required",
          detail: "If-Match is required.",
          traceId: "trace-428",
        }),
    });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Withdraw dispute" }));

    await user.click(screen.getByRole("button", { name: "Yes, withdraw" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("We could not withdraw this dispute");
    expect(alert).toHaveTextContent("Reference: trace-428");
    expect(screen.queryByText(/This dispute changed/)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reload" })).not.toBeInTheDocument();
  });

  it("sends no If-Match when the page never got an ETag (and so the server's 428 is a failure, not a guess)", async () => {
    const detail = makeDisputeDetail();
    const fake = fakeDisputesApi({ disputes: [detail] });
    server.use(
      signedIn(),
      http.get("/api/v1/disputes/:id", () => HttpResponse.json(detail)), // deliberately no ETag
      ...fake.handlers,
    );
    renderApp(`/disputes/${detail.id}`);
    await screen.findByRole("heading", { level: 1, name: /^Dispute:/ });
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Withdraw dispute" }));
    await user.click(screen.getByRole("button", { name: "Yes, withdraw" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "We could not withdraw this dispute",
    );
    expect(fake.api.withdrawCalls).toEqual([{ id: detail.id, ifMatch: null }]);
  });

  it("a server error keeps the page, shows the reference, and Try again resends", async () => {
    let fail = true;
    const { api, detail } = await openDispute(makeDisputeDetail(), {
      interceptWithdraw: () =>
        fail ? problem(503, { detail: "Try later.", traceId: "trace-503" }) : undefined,
    });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Withdraw dispute" }));
    await user.click(screen.getByRole("button", { name: "Yes, withdraw" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("We could not withdraw this dispute");
    expect(alert).toHaveTextContent("Reference: trace-503");
    expect(screen.getByRole("heading", { level: 1 })).toBeInTheDocument();

    fail = false;
    await user.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(statusRegion()).toHaveTextContent("Dispute withdrawn."));
    expect(api.withdrawCalls.filter((c) => c.id === detail.id)).toHaveLength(2);
  });
});
