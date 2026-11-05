import { afterEach, describe, expect, it, vi } from "vitest";

import { newUuid } from "./uuid";

const V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

describe("newUuid", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("makes a v4 uuid each time", () => {
    const ids = new Set(Array.from({ length: 50 }, newUuid));
    expect(ids.size).toBe(50);
    for (const id of ids) expect(id).toMatch(V4);
  });

  it("still works where crypto.randomUUID is missing (plain http outside localhost)", () => {
    vi.stubGlobal("crypto", { getRandomValues: crypto.getRandomValues.bind(crypto) });
    const ids = new Set(Array.from({ length: 50 }, newUuid));
    expect(ids.size).toBe(50);
    for (const id of ids) expect(id).toMatch(V4);
  });
});
