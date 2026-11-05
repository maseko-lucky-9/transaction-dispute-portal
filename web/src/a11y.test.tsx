import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http } from "msw";
import { axe } from "vitest-axe";
import { describe, expect, it } from "vitest";

import { ApiError } from "@/api/problem";
import { ErrorState } from "@/components/error-state";
import { problem, signedIn, signedOut } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";
import { fakeDisputesApi, makeDisputeDetail } from "@/test/disputes";
import { fakeTransactionsApi, makeTransaction, makeTransactions } from "@/test/transactions";

// jsdom has no layout, so axe's color-contrast rule cannot run here. Contrast in light and
// dark is checked in a real browser (playwright-cli), not in this file.
describe("accessibility (axe)", () => {
  it("has no violations in the signed-in shell", async () => {
    server.use(signedIn());
    const { container } = renderApp();
    await screen.findByText(/Signed in as/);

    expect(await axe(container)).toHaveNoViolations();
  });

  it("has no violations on the Sign in screen", async () => {
    server.use(signedOut());
    const { container } = renderApp();
    await screen.findByRole("heading", { name: "Sign in" });

    expect(await axe(container)).toHaveNoViolations();
  });

  it("has no violations when the session check fails", async () => {
    server.use(http.get("/bff/user", () => problem(500)));
    const { container } = renderApp();
    await screen.findByRole("alert");

    expect(await axe(container)).toHaveNoViolations();
  });

  it("has no violations on the not-found page", async () => {
    server.use(signedIn());
    const { container } = renderApp("/nope");
    await screen.findByRole("heading", { name: "Page not found" });

    expect(await axe(container)).toHaveNoViolations();
  });

  it("has no violations in a standalone ErrorState", async () => {
    const { container } = render(
      <ErrorState
        error={new ApiError(500, { title: "Oops", detail: "Broke.", traceId: "t-1" })}
        onRetry={() => undefined}
      />,
    );
    expect(await axe(container)).toHaveNoViolations();
  });

  describe("transactions page", () => {
    it("has no violations with a loaded list (debit, credit, pending, not disputable, disputed)", async () => {
      const items = [
        makeTransaction({}, 0),
        makeTransaction(
          { direction: "Credit", isDisputable: false, disputeBlockReasons: ["not_debit"] },
          1,
        ),
        makeTransaction(
          {
            status: "Pending",
            postedAt: null,
            isDisputable: false,
            disputeBlockReasons: ["not_posted"],
          },
          2,
        ),
        makeTransaction({ isDisputable: false, disputeBlockReasons: ["window_expired"] }, 3),
        makeTransaction({ dispute: { id: "d1", status: "Submitted" } }, 4),
        ...makeTransactions(30).slice(5),
      ];
      server.use(signedIn(), fakeTransactionsApi(items).handler);
      const { container } = renderApp("/transactions");
      await screen.findByRole("table");

      expect(await axe(container)).toHaveNoViolations();
    });

    it("has no violations when empty", async () => {
      server.use(signedIn(), fakeTransactionsApi([]).handler);
      const { container } = renderApp("/transactions?status=Posted");
      await screen.findByText("No transactions match these filters");

      expect(await axe(container)).toHaveNoViolations();
    });

    it("has no violations when the list failed to load", async () => {
      server.use(
        signedIn(),
        http.get("/api/v1/transactions", () => problem(503, { code: "idp_unavailable" })),
      );
      const { container } = renderApp("/transactions");
      await screen.findByRole("alert");

      expect(await axe(container)).toHaveNoViolations();
    });

    it("has no violations while the filter form shows errors", async () => {
      server.use(signedIn(), fakeTransactionsApi(makeTransactions(5)).handler);
      const { container } = renderApp("/transactions");
      await screen.findByRole("table");
      fireEvent.change(screen.getByLabelText("From"), { target: { value: "2026-10-31" } });
      fireEvent.change(screen.getByLabelText("To"), { target: { value: "2026-10-01" } });
      await userEvent.setup().click(screen.getByRole("button", { name: "Apply filters" }));
      await screen.findByRole("alert");

      expect(screen.getByLabelText("To")).toHaveAttribute("aria-invalid", "true");
      expect(await axe(container)).toHaveNoViolations();
    });
  });

  describe("disputes pages", () => {
    const statuses = ["Submitted", "UnderReview", "Accepted", "Rejected", "Withdrawn"] as const;

    it("has no violations in the history with every status", async () => {
      const disputes = statuses.map((status, index) =>
        makeDisputeDetail({ status, merchantName: `Shop ${index}` }, index),
      );
      server.use(signedIn(), ...fakeDisputesApi({ disputes }).handlers);
      const { container } = renderApp("/disputes");
      await screen.findByRole("table");

      expect(await axe(container)).toHaveNoViolations();
    });

    it("has no violations in the history when empty", async () => {
      server.use(signedIn(), ...fakeDisputesApi().handlers);
      const { container } = renderApp("/disputes");
      await screen.findByText("You have not raised any disputes yet");

      expect(await axe(container)).toHaveNoViolations();
    });

    it("has no violations in the history when it failed to load", async () => {
      server.use(
        signedIn(),
        http.get("/api/v1/disputes", () => problem(503, { code: "idp_unavailable" })),
        ...fakeDisputesApi().handlers, // the reasons lookup still answers (first match wins)
      );
      const { container } = renderApp("/disputes");
      await screen.findByRole("alert");

      expect(await axe(container)).toHaveNoViolations();
    });

    it("has no violations on a dispute's detail, open and closed", async () => {
      const open = makeDisputeDetail({ status: "UnderReview" });
      const closed = makeDisputeDetail({ status: "Rejected", resolutionNote: "No proof." }, 1);
      server.use(signedIn(), ...fakeDisputesApi({ disputes: [open, closed] }).handlers);

      const first = renderApp(`/disputes/${open.id}`);
      await first.findByRole("button", { name: "Withdraw dispute" });
      expect(await axe(first.container)).toHaveNoViolations();
      first.unmount();

      const second = renderApp(`/disputes/${closed.id}`);
      await second.findByText("Resolution note");
      expect(await axe(second.container)).toHaveNoViolations();
    });

    it("has no violations while the withdraw confirmation is open and after a stale-data error", async () => {
      const dispute = makeDisputeDetail();
      const { api, handlers } = fakeDisputesApi({ disputes: [dispute] });
      server.use(signedIn(), ...handlers);
      const { container } = renderApp(`/disputes/${dispute.id}`);
      const user = userEvent.setup();

      await user.click(await screen.findByRole("button", { name: "Withdraw dispute" }));
      expect(await axe(container)).toHaveNoViolations();

      api.agentActs(dispute.id, "UnderReview");
      await user.click(screen.getByRole("button", { name: "Yes, withdraw" }));
      await screen.findByRole("alert");
      expect(await axe(container)).toHaveNoViolations();
    });

    it("has no violations on the not-found page and the error page", async () => {
      server.use(signedIn(), ...fakeDisputesApi().handlers);
      const missing = renderApp("/disputes/aaaaaaaa-0000-4000-8000-000000000999");
      await missing.findByRole("heading", { name: "Dispute not found" });
      expect(await axe(missing.container)).toHaveNoViolations();
      missing.unmount();

      server.use(http.get("/api/v1/disputes/:id", () => problem(503, { traceId: "t-503" })));
      const failing = renderApp("/disputes/aaaaaaaa-0000-4000-8000-000000000998");
      await failing.findByRole("alert");
      expect(await axe(failing.container)).toHaveNoViolations();
    });
  });

  describe("dispute form page", () => {
    const open = (overrides = {}, options: Parameters<typeof fakeDisputesApi>[0] = {}) => {
      const tx = makeTransaction({ merchantName: "Netflix", ...overrides });
      server.use(
        signedIn(),
        ...fakeTransactionsApi([tx]).handlers,
        ...fakeDisputesApi({ transactions: [tx], ...options }).handlers,
      );
      const view = renderApp(`/transactions/${tx.id}/dispute`);
      return { ...view, tx, user: userEvent.setup() };
    };

    it("has no violations when empty", async () => {
      const { container, findByRole } = open();
      await findByRole("form", { name: "Dispute this transaction" });

      expect(await axe(container)).toHaveNoViolations();
    });

    it("has no violations while fields show errors", async () => {
      const { container, findByRole, user } = open();
      await findByRole("form", { name: "Dispute this transaction" });
      await user.click(screen.getByRole("button", { name: "Submit dispute" }));
      await screen.findByText("Choose a reason", { selector: "p" });

      expect(screen.getByLabelText(/^Reason/)).toHaveAttribute("aria-invalid", "true");
      expect(await axe(container)).toHaveNoViolations();
    });

    it("has no violations after the server refuses (rules list, retry)", async () => {
      const { container, findByRole, user } = open(
        {},
        {
          interceptCreate: () =>
            problem(422, {
              code: "eligibility_failed",
              violations: [{ code: "window_expired", message: "x" }],
            }),
        },
      );
      await findByRole("form", { name: "Dispute this transaction" });
      await user.selectOptions(screen.getByLabelText(/^Reason/), "duplicate");
      await user.click(screen.getByLabelText(/^What happened/));
      await user.paste("I was charged twice for it.");
      await user.click(screen.getByRole("button", { name: "Submit dispute" }));
      await screen.findByText("This dispute can't be submitted");

      expect(await axe(container)).toHaveNoViolations();
    });

    it("has no violations for a transaction that cannot be disputed, and for one that is not found", async () => {
      const blocked = open({ isDisputable: false, disputeBlockReasons: ["not_debit"] });
      await blocked.findByRole("region", { name: "This transaction can't be disputed" });
      expect(await axe(blocked.container)).toHaveNoViolations();
      blocked.unmount();

      server.use(signedIn(), ...fakeTransactionsApi([]).handlers, ...fakeDisputesApi().handlers);
      const missing = renderApp("/transactions/aaaaaaaa-0000-4000-8000-000000000999/dispute");
      await missing.findByRole("heading", { name: "Transaction not found" });
      expect(await axe(missing.container)).toHaveNoViolations();
    });
  });
});
