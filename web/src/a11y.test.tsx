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
});
