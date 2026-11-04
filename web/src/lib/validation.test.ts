import { describe, expect, it } from "vitest";

import cases from "./validation-cases.json";
import { containsSensitiveNumber, isLuhnValid } from "./validation";

interface ValidationCase {
  name: string;
  input: string;
  valid: boolean;
  code: string | null;
}

describe("isLuhnValid", () => {
  it.each(["4111111111111111", "378282246310005", "8001015009087", "0", "00"])(
    "accepts %s",
    (digits) => {
      expect(isLuhnValid(digits)).toBe(true);
    },
  );

  it.each(["4111111111111112", "1", "1234567890123"])("rejects %s", (digits) => {
    expect(isLuhnValid(digits)).toBe(false);
  });

  it.each(["", "4111 1111", "4111-1111", "abc", "４１１１１１１１１１１１１１１１"])(
    "rejects non-digit input %j",
    (input) => {
      expect(isLuhnValid(input)).toBe(false);
    },
  );
});

describe("containsSensitiveNumber against the shared validation-cases.json", () => {
  const shared = cases as ValidationCase[];

  it("keeps the case file well formed", () => {
    expect(shared.length).toBeGreaterThan(10);
    for (const c of shared) {
      // A case is valid exactly when it carries no error code.
      expect(c.valid).toBe(c.code === null);
    }
    expect(new Set(shared.map((c) => c.name)).size).toBe(shared.length);
  });

  it.each(shared.map((c) => [c.name, c] as const))("%s", (_name, c) => {
    expect(containsSensitiveNumber(c.input)).toBe(!c.valid);
  });
});

describe("containsSensitiveNumber edge behaviour", () => {
  it("does not treat Unicode digits as digits (parity with [0-9] on the server)", () => {
    expect(containsSensitiveNumber("４１１１ １１１１ １１１１ １１１１")).toBe(false);
  });

  it("does not join digits across a dot", () => {
    expect(containsSensitiveNumber("4111.1111.1111.1111")).toBe(false);
  });

  it("stays fast on a maximum-length description of single digits", () => {
    const worstCase = "1 ".repeat(1000);
    const started = performance.now();
    containsSensitiveNumber(worstCase);
    expect(performance.now() - started).toBeLessThan(250);
  });
});
