import { describe, expect, it } from "vitest";

import { searchWithStatus } from "./status-links";

describe("searchWithStatus", () => {
  const current = {
    from: "2026-01-01",
    to: "2026-02-01",
    limit: 50,
    status: "Posted" as string | undefined,
  };

  it("swaps the status and keeps every other param", () => {
    expect(searchWithStatus(current, "Pending")).toEqual({
      from: "2026-01-01",
      to: "2026-02-01",
      limit: 50,
      status: "Pending",
    });
  });

  it("drops the status key for All, keeping the rest", () => {
    const next = searchWithStatus(current, undefined);
    expect("status" in next).toBe(false);
    expect(next).toEqual({ from: "2026-01-01", to: "2026-02-01", limit: 50 });
  });

  it("also drops the key when the status type has no undefined (a default such as Open)", () => {
    const queue = { status: "Accepted" as "open" | "Accepted", limit: 10 };
    const next = searchWithStatus(queue, undefined);
    expect("status" in next).toBe(false);
    expect(next).toEqual({ limit: 10 });
  });

  it("does not mutate the current search", () => {
    searchWithStatus(current, undefined);
    expect(current.status).toBe("Posted");
  });
});
