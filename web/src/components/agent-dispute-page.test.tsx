import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http } from "msw";
import { describe, expect, it } from "vitest";

import { fakeAgentApi, makeAgentDispute } from "@/test/agent";
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
