import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { ApiError } from "@/api/problem";

import { ErrorState } from "./error-state";

describe("ErrorState", () => {
  it("shows the problem message and the traceId as a reference", () => {
    const error = new ApiError(500, {
      title: "Internal Server Error",
      detail: "The dispute could not be saved.",
      traceId: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    });
    render(<ErrorState error={error} />);

    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Internal Server Error");
    expect(alert).toHaveTextContent("The dispute could not be saved.");
    expect(alert).toHaveTextContent(
      "Reference: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    );
  });

  it("omits the reference when the server sent no traceId", () => {
    render(<ErrorState error={new ApiError(500, { title: "Oops", detail: "Try later." })} />);
    expect(screen.queryByText(/Reference:/)).not.toBeInTheDocument();
  });

  it("does not leak the message of an unexpected (non-API) error", () => {
    render(<ErrorState error={new TypeError("Cannot read properties of undefined (secret)")} />);
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Something went wrong. Please try again.");
    expect(alert).not.toHaveTextContent("secret");
  });

  it("lets the caller override the heading", () => {
    render(<ErrorState error={new ApiError(500, { title: "Server" })} title="Could not load" />);
    expect(screen.getByRole("heading", { name: "Could not load" })).toBeInTheDocument();
  });

  it("uses h2 by default and h1 when it is the whole page", () => {
    const { rerender } = render(<ErrorState error={new Error("x")} />);
    expect(screen.getByRole("heading", { level: 2 })).toBeInTheDocument();

    rerender(<ErrorState error={new Error("x")} headingLevel="h1" />);
    expect(screen.getByRole("heading", { level: 1 })).toBeInTheDocument();
  });

  it("renders Try again only when a retry handler is given, and calls it", async () => {
    const onRetry = vi.fn();
    const { rerender } = render(<ErrorState error={new Error("x")} />);
    expect(screen.queryByRole("button", { name: "Try again" })).not.toBeInTheDocument();

    rerender(<ErrorState error={new Error("x")} onRetry={onRetry} />);
    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));
    expect(onRetry).toHaveBeenCalledOnce();
  });

  it("lets the caller replace the message and add content under it, keeping the reference", () => {
    render(
      <ErrorState
        error={new ApiError(422, { detail: "Server words.", traceId: "t-22" })}
        message="Our words."
      >
        <a href="/next">Go on</a>
      </ErrorState>,
    );
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Our words.");
    expect(alert).not.toHaveTextContent("Server words.");
    expect(alert).toHaveTextContent("Reference: t-22");
    expect(screen.getByRole("link", { name: "Go on" })).toBeInTheDocument();
  });

  it("makes Try again a plain button, so inside a form it cannot also submit the form", async () => {
    const submit = vi.fn((event: React.FormEvent) => event.preventDefault());
    const retry = vi.fn();
    render(
      <form onSubmit={submit}>
        <ErrorState error={new Error("x")} onRetry={retry} />
      </form>,
    );

    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));

    expect(retry).toHaveBeenCalledOnce();
    expect(submit).not.toHaveBeenCalled();
  });
});
