import { describe, expect, it } from "vitest";

import { accessFor } from "./roles";

describe("accessFor", () => {
  it.each([
    [["customer"], "customer"],
    [["dispute-agent"], "agent"],
    [["customer", "dispute-agent"], "both"],
    [["dispute-agent", "customer"], "both"],
    [[], "customer"], // no role yet: the customer pages, where the server decides
    [["something-else"], "customer"],
    [["offline_access", "dispute-agent", "uma_authorization"], "agent"],
  ])("roles %j open the %s area", (roles, expected) => {
    expect(accessFor({ roles })).toBe(expected);
  });

  it("matches the role name exactly (no lookalikes)", () => {
    expect(accessFor({ roles: ["dispute_agent"] })).toBe("customer");
    expect(accessFor({ roles: ["Dispute-Agent"] })).toBe("customer");
    expect(accessFor({ roles: ["dispute-agent-admin"] })).toBe("customer");
  });
});
