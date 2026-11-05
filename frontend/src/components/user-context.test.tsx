import { renderHook } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { alice } from "@/test/handlers";

import { UserContext, useUser } from "./user-context";

describe("useUser", () => {
  it("returns the user provided by the gate", () => {
    const { result } = renderHook(() => useUser(), {
      wrapper: ({ children }) => (
        <UserContext.Provider value={alice}>{children}</UserContext.Provider>
      ),
    });
    expect(result.current).toEqual(alice);
  });

  it("throws outside the gate rather than rendering as a ghost user", () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined); // React logs the throw
    expect(() => renderHook(() => useUser())).toThrow(/inside <SessionGate>/);
  });
});
