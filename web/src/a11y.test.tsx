import { render, screen } from "@testing-library/react";
import { http } from "msw";
import { axe } from "vitest-axe";
import { describe, expect, it } from "vitest";

import { ApiError } from "@/api/problem";
import { ErrorState } from "@/components/error-state";
import { problem, signedIn, signedOut } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";

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
});
