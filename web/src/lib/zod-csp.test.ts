import * as z from "zod";
import { describe, expect, it } from "vitest";

import "./zod-csp";

describe("zod CSP configuration", () => {
  it("disables zod's eval-based JIT so a strict CSP sees no violation", () => {
    expect(z.core.globalConfig.jitless).toBe(true);
  });
});
