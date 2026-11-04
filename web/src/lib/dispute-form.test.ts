import { describe, expect, it } from "vitest";

import {
  DESCRIPTION_MAX,
  describeRemaining,
  disputeFormSchema,
  SENSITIVE_MESSAGE,
} from "./dispute-form";
import cases from "./validation-cases.json";

const schema = disputeFormSchema({
  reasonCodes: ["unauthorised", "duplicate"],
  maxAmount: "123.45",
  currency: "ZAR",
});
const valid = { reasonCode: "duplicate", description: "Charged twice for one order.", amount: "" };

function messages(values: Partial<typeof valid>) {
  const result = schema.safeParse({ ...valid, ...values });
  if (result.success) return {};
  return Object.fromEntries(
    result.error.issues.map((issue) => [String(issue.path[0]), issue.message]),
  );
}

describe("disputeFormSchema", () => {
  it("accepts a complete form and trims the description", () => {
    const result = schema.safeParse({ ...valid, description: "  Charged twice for one order.  " });
    expect(result.success && result.data.description).toBe("Charged twice for one order.");
  });

  it("requires a reason, and one the server offered", () => {
    expect(messages({ reasonCode: "" })).toEqual({ reasonCode: "Choose a reason" });
    expect(messages({ reasonCode: "made_up" })).toEqual({
      reasonCode: "Choose a reason from the list",
    });
  });

  it.each([
    ["", false],
    ["         ", false],
    ["123456789", false],
    ["  123456789  ", false], // trimmed first: 9 characters
    ["1234567890", true],
    ["x".repeat(DESCRIPTION_MAX), true],
    ["x".repeat(DESCRIPTION_MAX + 1), false],
    [` ${"x".repeat(DESCRIPTION_MAX)} `, true], // the padding is not counted
  ])("description %j is ok: %s", (description, ok) => {
    expect("description" in messages({ description })).toBe(!ok);
  });

  it("says why a description is too short or too long", () => {
    expect(messages({ description: "short" })["description"]).toBe(
      "Describe what went wrong, in at least 10 characters",
    );
    expect(messages({ description: "x".repeat(2001) })["description"]).toBe(
      "Keep the description to 2000 characters or fewer",
    );
  });

  it.each(cases.map((c) => [c.name, c.input, c.valid] as const))(
    "sensitive-number rule, shared case: %s",
    (_name, input, ok) => {
      // Padding keeps the length rule out of the way; it has no digits, so it cannot join a chain.
      const description = `Problem with my order. ${input}`;
      const message = messages({ description })["description"];
      expect(message === SENSITIVE_MESSAGE).toBe(!ok);
    },
  );

  it.each([
    ["", true],
    ["49.99", true],
    ["50", true],
    ["50,5", true],
    ["123.45", true], // the whole transaction is allowed
    ["123.450", false], // 3 decimals
    ["123.46", false],
    ["1000", false],
    ["0", false],
    ["0.00", false],
    ["-1", false],
    ["abc", false],
    ["1e2", false],
  ])("amount %j is ok: %s", (amount, ok) => {
    expect("amount" in messages({ amount })).toBe(!ok);
  });

  it("gives each amount problem its own sentence, naming the maximum", () => {
    expect(messages({ amount: "abc" })["amount"]).toBe(
      "Enter an amount like 49.99, with at most 2 decimals",
    );
    expect(messages({ amount: "0" })["amount"]).toBe("The amount must be more than zero");
    expect(messages({ amount: "123.46" })["amount"]).toMatch(
      /^The amount cannot be more than R\s123,45$/,
    );
  });

  it("compares as decimals: scale does not matter, and the last cent does", () => {
    const base = { reasonCode: "a", description: "A long enough text." };
    const scaled = disputeFormSchema({ reasonCodes: ["a"], maxAmount: "0.3000", currency: "ZAR" });
    expect(scaled.safeParse({ ...base, amount: "0.3" }).success).toBe(true);
    expect(scaled.safeParse({ ...base, amount: "0.31" }).success).toBe(false);

    const large = disputeFormSchema({
      reasonCodes: ["a"],
      maxAmount: "1234567890123.45",
      currency: "ZAR",
    });
    expect(large.safeParse({ ...base, amount: "1234567890123.45" }).success).toBe(true);
    expect(large.safeParse({ ...base, amount: "1234567890123.46" }).success).toBe(false);
  });
});

describe("describeRemaining", () => {
  it.each([
    [0, ""],
    [1799, ""], // 201 left: say nothing
    [1800, "200 or fewer characters left"],
    [1850, "200 or fewer characters left"],
    [1900, "100 or fewer characters left"],
    [1950, "50 or fewer characters left"],
    [1990, "10 or fewer characters left"],
    [1999, "10 or fewer characters left"],
    [2000, "No characters left"],
    [2001, "Over the limit"],
    [5000, "Over the limit"],
  ])("at %i characters: %j", (length, expected) => {
    expect(describeRemaining(length)).toBe(expected);
  });
});
