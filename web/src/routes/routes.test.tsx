import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { ApiError } from "@/api/problem";

import { fakeDisputesApi, makeDisputeDetail } from "@/test/disputes";
import { signedIn, signedOut } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";
import { fakeTransactionsApi } from "@/test/transactions";

import { homeSearchSchema, indexRoute } from "./index";
import { rootRoute } from "./root";

describe("routes", () => {
  it("declares search validation on the home route", () => {
    expect(indexRoute.options.validateSearch).toBe(homeSearchSchema);
    expect(homeSearchSchema.parse({ junk: "1" })).toEqual({});
  });

  it("renders home with a junk query string instead of crashing", async () => {
    server.use(signedIn());
    renderApp("/?junk=1&limit=999");
    expect(await screen.findByText(/Signed in as/)).toBeInTheDocument();
  });

  it("renders the not-found page inside the shell for unknown paths", async () => {
    server.use(signedIn());
    renderApp("/does-not-exist");
    expect(await screen.findByRole("heading", { name: "Page not found" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back to home" })).toHaveAttribute("href", "/");
    expect(screen.getByTestId("header-user")).toBeInTheDocument();
  });

  it("renders route errors through ErrorState, with the trace reference and a reset", async () => {
    const ErrorComponent = rootRoute.options.errorComponent;
    if (!ErrorComponent) throw new Error("rootRoute needs an errorComponent");
    const reset = vi.fn();

    render(
      <ErrorComponent
        error={new ApiError(500, { title: "Broke", detail: "It broke.", traceId: "trace-route" })}
        reset={reset}
      />,
    );

    expect(screen.getByRole("alert")).toHaveTextContent("Reference: trace-route");
    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));
    expect(reset).toHaveBeenCalledOnce();
  });

  describe("primary navigation", () => {
    it.each([
      ["/", null, "Signed in as"],
      ["/transactions", "Transactions", "You have no transactions yet"],
      ["/disputes", "Disputes", "You have not raised any disputes yet"],
    ])("at %s marks %s as the current page", async (path, current, loaded) => {
      server.use(signedIn(), ...fakeDisputesApi().handlers, fakeTransactionsApi([]).handler);
      renderApp(path);
      // Wait for the page's own request to finish too, so none outlives the test.
      await screen.findByText(new RegExp(loaded));
      const nav = screen.getByRole("navigation", { name: "Primary" });

      const marked = within(nav)
        .getAllByRole("link")
        .filter((link) => link.getAttribute("aria-current") === "page")
        .map((link) => link.textContent);
      expect(marked).toEqual(current ? [current] : []);
      expect(within(nav).getByRole("link", { name: "Transactions" })).toHaveAttribute(
        "href",
        "/transactions",
      );
      expect(within(nav).getByRole("link", { name: "Disputes" })).toHaveAttribute(
        "href",
        "/disputes",
      );
    });

    it("keeps Disputes current on a dispute's detail page", async () => {
      const dispute = makeDisputeDetail();
      server.use(signedIn(), ...fakeDisputesApi({ disputes: [dispute] }).handlers);
      renderApp(`/disputes/${dispute.id}`);
      await screen.findByRole("heading", { level: 1, name: /^Dispute:/ });
      const nav = screen.getByRole("navigation", { name: "Primary" });

      expect(within(nav).getByRole("link", { name: "Disputes" })).toHaveAttribute(
        "aria-current",
        "page",
      );
    });

    it("is not shown to a signed-out visitor", async () => {
      server.use(signedOut());
      renderApp("/disputes");
      await screen.findByRole("heading", { name: "Sign in" });
      expect(screen.queryByRole("navigation", { name: "Primary" })).not.toBeInTheDocument();
    });

    it("home links to the transactions and to my disputes", async () => {
      server.use(signedIn());
      renderApp("/");
      expect(await screen.findByRole("link", { name: /My disputes/ })).toHaveAttribute(
        "href",
        "/disputes",
      );
      expect(screen.getByRole("link", { name: "My transactions" })).toHaveAttribute(
        "href",
        "/transactions",
      );
    });
  });
});
