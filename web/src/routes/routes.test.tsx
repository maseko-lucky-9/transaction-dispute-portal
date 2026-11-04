import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { ApiError } from "@/api/problem";

import { signedIn } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";

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
});
