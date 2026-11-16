import { act, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http } from "msw";
import { describe, expect, it } from "vitest";

import { agentKeys } from "@/api/agent";
import { fakeAgentApi, makeAgentCustomerTransaction, makeAgentDispute } from "@/test/agent";
import { bob, problem, signedIn } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";

const ID = "dddddddd-0000-4000-8000-000000000001";

async function openDispute(status: "Submitted" | "UnderReview" | "Accepted" = "Submitted") {
  const fake = fakeAgentApi([makeAgentDispute({ status })]);
  server.use(signedIn(bob), ...fake.handlers);
  const view = renderApp(`/agent/disputes/${ID}`);
  await screen.findByRole("heading", { level: 1, name: /^Dispute: Merchant 0001/ });
  return { ...fake, ...view };
}

const note = () => screen.getByLabelText(/^Note to the customer/);

describe("agent dispute page", () => {
  it("shows the customer, the account, the overview and the agent timeline", async () => {
    await openDispute();
    expect(screen.getByText("Customer 1")).toBeInTheDocument();
    expect(screen.getByText("****1000")).toBeInTheDocument();
    expect(screen.getByText("What the customer wrote")).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Timeline" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back to the review queue" })).toHaveAttribute(
      "href",
      "/agent",
    );
  });

  it("offers only what the lifecycle allows from the status", async () => {
    await openDispute("Submitted");
    expect(screen.getByRole("button", { name: "Start review" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Accept" })).not.toBeInTheDocument();
  });

  it("offers nothing for a decided dispute", async () => {
    await openDispute("Accepted");
    expect(screen.queryByRole("button", { name: "Start review" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Accept" })).not.toBeInTheDocument();
    expect(screen.getByText(/nothing more to do/)).toBeInTheDocument();
  });

  it.each([
    ["withdrawn after review started", true, "Stopped at step 2 of 3"],
    ["withdrawn before review", false, "Stopped at step 1 of 3"],
  ])("the stepper reads the timeline: %s", async (_case, reviewed, label) => {
    const base = makeAgentDispute({ status: "Withdrawn" });
    const first = base.timeline[0]!;
    const timeline = [
      first,
      ...(reviewed ? [{ ...first, from: "Submitted" as const, to: "UnderReview" as const }] : []),
      {
        ...first,
        from: reviewed ? ("UnderReview" as const) : ("Submitted" as const),
        to: "Withdrawn" as const,
      },
    ];
    server.use(signedIn(bob), ...fakeAgentApi([{ ...base, timeline }]).handlers);
    renderApp(`/agent/disputes/${ID}`);
    expect(await screen.findByRole("img", { name: label })).toBeInTheDocument();
  });

  it("is not found for a malformed or unknown id", async () => {
    server.use(signedIn(bob), ...fakeAgentApi([]).handlers);
    renderApp("/agent/disputes/not-an-id");
    expect(
      await screen.findByRole("heading", { level: 1, name: "Dispute not found" }),
    ).toBeInTheDocument();
  });

  it("starts the review with If-Match and no note needed", async () => {
    const { api } = await openDispute("Submitted");
    await userEvent.setup().click(screen.getByRole("button", { name: "Start review" }));

    await screen.findByText(/Status updated\./);
    expect(api.transitionCalls).toEqual([
      { id: ID, ifMatch: '"v1"', body: { toStatus: "UnderReview" } },
    ]);
    // The page now shows the new status and the next moves.
    expect(await screen.findByRole("button", { name: "Accept" })).toBeInTheDocument();
    expect(screen.getAllByText("Under review").length).toBeGreaterThan(0);
  });

  it("clears the note once it has been sent, so it is not reused for the next move", async () => {
    await openDispute("Submitted");
    const user = userEvent.setup();
    await user.type(note(), "Looking into it");
    await user.click(screen.getByRole("button", { name: "Start review" }));

    await screen.findByRole("button", { name: "Accept" });
    expect(note()).toHaveValue("");
  });

  it("requires a note to accept, sends nothing, and labels the error on the field", async () => {
    const { api } = await openDispute("UnderReview");
    await userEvent.setup().click(screen.getByRole("button", { name: "Accept" }));

    expect(await screen.findByText("Enter a note: the customer will see it")).toBeInTheDocument();
    expect(note()).toHaveAttribute("aria-invalid", "true");
    expect(note()).toHaveAccessibleDescription(/Required to accept or reject/);
    expect(note()).toHaveFocus();
    expect(api.transitionCalls).toEqual([]);
  });

  it("keeps crimson for Accept only: Reject is outlined, on the choice and on the confirmation", async () => {
    await openDispute("UnderReview");
    const user = userEvent.setup();
    expect(screen.getByRole("button", { name: "Accept" })).toHaveClass("bg-primary");
    expect(screen.getByRole("button", { name: "Reject" })).not.toHaveClass("bg-primary");

    await user.type(note(), "The charge was authorised.");
    await user.click(screen.getByRole("button", { name: "Reject" }));
    expect(await screen.findByRole("button", { name: "Yes, reject" })).not.toHaveClass(
      "bg-primary",
    );
  });

  it("refuses a note with a card number before any request", async () => {
    const { api } = await openDispute("UnderReview");
    const user = userEvent.setup();
    await user.type(note(), "card 4111 1111 1111 1111");
    await user.click(screen.getByRole("button", { name: "Reject" }));

    expect(await screen.findByText("Please remove card or ID numbers")).toBeInTheDocument();
    expect(api.transitionCalls).toEqual([]);
  });

  it("confirms a decision, then sends the trimmed note", async () => {
    const { api } = await openDispute("UnderReview");
    const user = userEvent.setup();
    await user.type(note(), "  Refunded in full.  ");
    await user.click(screen.getByRole("button", { name: "Accept" }));

    // Nothing is sent until confirmed.
    expect(await screen.findByText("Do you want to accept this dispute?")).toBeInTheDocument();
    expect(api.transitionCalls).toEqual([]);
    await user.click(screen.getByRole("button", { name: "Yes, accept" }));

    await screen.findByText(/Status updated\./);
    expect(api.transitionCalls).toEqual([
      {
        id: ID,
        ifMatch: '"v1"',
        body: { toStatus: "Accepted", note: "Refunded in full." },
      },
    ]);
    expect(screen.queryByRole("button", { name: "Accept" })).not.toBeInTheDocument();
    expect(screen.getAllByText("Refunded in full.").length).toBeGreaterThan(0);
  });

  it("cancelling the confirmation sends nothing", async () => {
    const { api } = await openDispute("UnderReview");
    const user = userEvent.setup();
    await user.type(note(), "No.");
    await user.click(screen.getByRole("button", { name: "Reject" }));
    await user.click(screen.getByRole("button", { name: "Cancel" }));

    expect(screen.getByRole("button", { name: "Reject" })).toBeInTheDocument();
    expect(api.transitionCalls).toEqual([]);
  });

  it("412: reloads the dispute, keeps the note, and says it changed", async () => {
    const { api } = await openDispute("Submitted");
    // Another agent starts the review behind this agent's back.
    api.otherAgentActs(ID, "UnderReview");
    const user = userEvent.setup();
    await user.type(note(), "My note");
    await user.click(screen.getByRole("button", { name: "Start review" }));

    expect(
      await screen.findByText("This dispute changed while you were reviewing it. We reloaded it."),
    ).toBeInTheDocument();
    expect(api.transitionCalls[0]?.ifMatch).toBe('"v1"');
    // The reload brought the new status and its buttons; the note typed is still there.
    await user.click(await screen.findByRole("button", { name: "Accept" }));
    expect(note()).toHaveValue("My note");
    await user.click(await screen.findByRole("button", { name: "Yes, accept" }));
    await screen.findByText(/Status updated\./);
    expect(api.transitionCalls.at(-1)?.ifMatch).toBe('"v2"');
  });

  it("sends the version the agent was shown, not a newer one a refetch brought in meanwhile", async () => {
    const { api, queryClient } = await openDispute("UnderReview");
    const user = userEvent.setup();
    await user.type(note(), "Refunded in full.");
    await user.click(screen.getByRole("button", { name: "Accept" }));
    await screen.findByText("Do you want to accept this dispute?");

    // While the agent reads the confirmation, a background refetch brings a newer ETag (same status).
    api.versionMovesOn(ID);
    await act(async () => {
      await queryClient.refetchQueries({ queryKey: agentKeys.detail(ID) });
    });
    expect(queryClient.getQueryData(agentKeys.detail(ID))).toMatchObject({ etag: '"v2"' });

    // The decision was made on v1: that is what goes out, the server refuses it, and nothing is decided.
    await user.click(screen.getByRole("button", { name: "Yes, accept" }));
    expect(
      await screen.findByText("This dispute changed while you were reviewing it. We reloaded it."),
    ).toBeInTheDocument();
    expect(api.transitionCalls).toEqual([
      { id: ID, ifMatch: '"v1"', body: { toStatus: "Accepted", note: "Refunded in full." } },
    ]);
    expect(api.store[0]?.status).toBe("UnderReview");
    expect(screen.queryByText(/Status updated\./)).not.toBeInTheDocument();

    // Having seen the reload, deciding again goes out on the version now shown.
    await user.click(await screen.findByRole("button", { name: "Accept" }));
    await user.click(await screen.findByRole("button", { name: "Yes, accept" }));
    await screen.findByText(/Status updated\./);
    expect(api.transitionCalls.at(-1)?.ifMatch).toBe('"v2"');
  });

  it("409: reloads and explains the move is no longer possible", async () => {
    // The page was read as UnderReview; the dispute was closed with the SAME etag path.
    server.use(signedIn(bob));
    const fake = fakeAgentApi([makeAgentDispute({ status: "UnderReview" })]);
    server.use(
      http.post(`/api/v1/agent/disputes/${ID}/transitions`, () =>
        problem(409, {
          title: "Conflict",
          detail: "A Accepted dispute cannot move to Rejected.",
          code: "invalid_transition",
        }),
      ),
      ...fake.handlers,
    );
    renderApp(`/agent/disputes/${ID}`);
    await screen.findByRole("heading", { level: 1 });
    const user = userEvent.setup();
    await user.type(note(), "Nope");
    await user.click(screen.getByRole("button", { name: "Reject" }));
    await user.click(screen.getByRole("button", { name: "Yes, reject" }));

    expect(
      await screen.findByText("That move is no longer possible. We reloaded the dispute."),
    ).toBeInTheDocument();
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "A Accepted dispute cannot move to Rejected.",
    );
  });

  it("400 from the server: shows the note's message beside the field", async () => {
    server.use(signedIn(bob));
    const fake = fakeAgentApi([makeAgentDispute({ status: "UnderReview" })]);
    server.use(
      http.post(`/api/v1/agent/disputes/${ID}/transitions`, () =>
        problem(400, {
          title: "One or more validation errors occurred.",
          code: "validation_failed",
          errors: { note: ["The server dislikes this note."] },
        }),
      ),
      ...fake.handlers,
    );
    renderApp(`/agent/disputes/${ID}`);
    await screen.findByRole("heading", { level: 1 });
    const user = userEvent.setup();
    await user.type(note(), "Fine note");
    await user.click(screen.getByRole("button", { name: "Accept" }));
    await user.click(screen.getByRole("button", { name: "Yes, accept" }));

    expect(await screen.findByText("The server dislikes this note.")).toBeInTheDocument();
    await waitFor(() => expect(note()).toHaveAttribute("aria-invalid", "true"));
    expect(note()).toHaveValue("Fine note");
  });

  it("428: shows a generic failure with its reference, not a field error", async () => {
    server.use(signedIn(bob));
    const fake = fakeAgentApi([makeAgentDispute({ status: "Submitted" })]);
    server.use(
      http.post(`/api/v1/agent/disputes/${ID}/transitions`, () =>
        problem(428, { title: "Precondition Required", code: "precondition_required" }),
      ),
      ...fake.handlers,
    );
    renderApp(`/agent/disputes/${ID}`);
    await screen.findByRole("heading", { level: 1 });
    await userEvent.setup().click(screen.getByRole("button", { name: "Start review" }));

    const alert = await screen.findByRole("alert");
    expect(within(alert).getByText("We could not update this dispute")).toBeInTheDocument();
    expect(within(alert).getByText(/Reference:/)).toBeInTheDocument();
  });

  it("sends no If-Match when the dispute was read without an ETag (server answers 428)", async () => {
    server.use(signedIn(bob));
    const fake = fakeAgentApi([makeAgentDispute({ status: "Submitted" })]);
    server.use(
      http.get(`/api/v1/agent/disputes/${ID}`, () =>
        Response.json(fake.api.store[0], { status: 200 }),
      ),
      ...fake.handlers,
    );
    renderApp(`/agent/disputes/${ID}`);
    await screen.findByRole("heading", { level: 1 });
    await userEvent.setup().click(screen.getByRole("button", { name: "Start review" }));

    await screen.findByRole("alert");
    expect(fake.api.transitionCalls[0]?.ifMatch).toBeNull();
  });
});

describe("agent dispute page: the customer's transactions", () => {
  // The first row is the disputed transaction (the fake dispute's transactionId).
  const TXN = "00000000-0000-4000-8000-000000000001";

  async function openWithTransactions(status: "Submitted" | "UnderReview" = "UnderReview") {
    const fake = fakeAgentApi(
      [makeAgentDispute({ status })],
      [makeAgentCustomerTransaction({ id: TXN, dispute: { id: ID, status } }, 0)],
    );
    server.use(signedIn(bob), ...fake.handlers);
    renderApp(`/agent/disputes/${ID}`);
    await screen.findByText("Shop 0001");
    return fake;
  }

  const row = () => screen.getByText("Shop 0001").closest("tr") as HTMLElement;
  const sectionHeading = () => screen.queryByRole("heading", { name: /^Transactions since/ });

  it("sits between the dispute's overview and the decision", async () => {
    await openWithTransactions();

    const overview = screen.getByRole("heading", { level: 2, name: "The transaction" });
    const list = screen.getByRole("heading", { level: 2, name: "Transactions since 04 Aug 2026" });
    const decision = screen.getByRole("heading", { level: 2, name: "Decision" });
    expect(overview.compareDocumentPosition(list) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(list.compareDocumentPosition(decision) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("reads the dispute's own list, not a customer's, and only once on open", async () => {
    const { api } = await openWithTransactions();

    expect(api.customerTransactionRequests.map((request) => request.id)).toEqual([ID]);
  });

  it("reads the list afresh when the review starts, so this dispute's row shows its new status", async () => {
    const { api } = await openWithTransactions("Submitted");
    expect(within(row()).getByText("Dispute: Submitted")).toBeInTheDocument();

    await userEvent.setup().click(screen.getByRole("button", { name: "Start review" }));

    await waitFor(() =>
      expect(within(row()).getByText("Dispute: Under review")).toBeInTheDocument(),
    );
    expect(within(row()).getByText("This dispute")).toBeInTheDocument();
    expect(api.customerTransactionRequests).toHaveLength(2);
  });

  it.each(["Accepted", "Rejected", "Withdrawn"] as const)(
    "shows no section, and asks for nothing, for a %s dispute",
    async (status) => {
      const fake = fakeAgentApi(
        [makeAgentDispute({ status })],
        [makeAgentCustomerTransaction({ id: TXN, dispute: { id: ID, status } }, 0)],
      );
      server.use(signedIn(bob), ...fake.handlers);
      renderApp(`/agent/disputes/${ID}`);
      await screen.findByText(/nothing more to do/);
      // A request would be made as the page mounts: give it the time it needs to show up.
      await new Promise((resolve) => setTimeout(resolve, 100));

      expect(sectionHeading()).not.toBeInTheDocument();
      expect(
        screen.queryByRole("heading", { name: "Customer transactions" }),
      ).not.toBeInTheDocument();
      expect(screen.queryByText("Shop 0001")).not.toBeInTheDocument();
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
      expect(fake.api.customerTransactionRequests).toEqual([]);
    },
  );

  it.each([
    ["Accept", "Yes, accept"],
    ["Reject", "Yes, reject"],
  ])(
    "takes the section away when %s closes the dispute, without another read",
    async (choice, confirm) => {
      const { api } = await openWithTransactions();
      expect(api.customerTransactionRequests).toHaveLength(1);
      const user = userEvent.setup();

      await user.type(note(), "Decided.");
      await user.click(screen.getByRole("button", { name: choice }));
      await user.click(await screen.findByRole("button", { name: confirm }));

      await screen.findByText(/Status updated\./);
      await new Promise((resolve) => setTimeout(resolve, 100));
      expect(sectionHeading()).not.toBeInTheDocument();
      expect(screen.queryByText("Shop 0001")).not.toBeInTheDocument();
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
      expect(api.customerTransactionRequests).toHaveLength(1);
    },
  );
});
