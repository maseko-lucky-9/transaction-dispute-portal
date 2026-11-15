import { screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { alice, bob, carol, signedIn } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";

describe("home page", () => {
  beforeEach(() => {
    // Only the clock is faked, so the greeting is deterministic and MSW/timers keep working.
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date(2026, 10, 15, 9, 30));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("greets the customer by first name for the time of day and shows the status legend", async () => {
    server.use(signedIn(alice));
    renderApp("/");
    expect(
      await screen.findByRole("heading", { level: 1, name: "Good morning, Alice" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "How a dispute moves" })).toBeInTheDocument();
  });

  it.each([
    ["an agent", bob],
    ["a user with both roles", carol],
  ])("does not show the customer status legend to %s", async (_who, user) => {
    server.use(signedIn(user));
    renderApp("/");
    await screen.findByRole("heading", { level: 1 });
    expect(screen.queryByRole("heading", { name: "How a dispute moves" })).not.toBeInTheDocument();
  });
});
