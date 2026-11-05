import { expect, it } from "vitest";

// Deliberately imports nothing but the entry module: if zod-csp were also imported here (as in
// zod-csp.test.ts) the configuration would be applied by that import and this could not fail.
import { z } from "./zod";

it("the module application code imports zod from has already applied the CSP configuration", () => {
  expect(z.core.globalConfig.jitless).toBe(true);
});
