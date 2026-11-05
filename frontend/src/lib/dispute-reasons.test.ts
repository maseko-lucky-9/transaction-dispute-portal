import { describe, expect, it } from "vitest";

import { describeBlockReasons, describeViolation } from "./dispute-reasons";

describe("describeViolation", () => {
  it.each([
    ["not_posted", "Pending transactions can't be disputed yet"],
    ["not_debit", "Only debits can be disputed"],
    ["window_expired", "The 120-day dispute window has closed"],
    ["already_disputed", "This transaction already has a dispute"],
    ["amount_exceeds_transaction", "The amount is more than the transaction"],
    ["amount_not_positive", "The amount must be more than zero"],
    [
      "too_many_active_disputes",
      "You have too many open disputes. Wait for some to be decided, or withdraw one",
    ],
    [
      "too_many_disputes_for_transaction",
      "This transaction has reached the most disputes it can have",
    ],
  ])("puts %s into plain language", (code, sentence) => {
    expect(describeViolation(code, "server words are ignored for known codes")).toBe(sentence);
  });

  it("falls back to the server's sentence for a code it does not know, then to a generic one", () => {
    expect(describeViolation("brand_new_rule", " The bank said no. ")).toBe("The bank said no.");
    expect(describeViolation("brand_new_rule")).toBe("This transaction can't be disputed");
    expect(describeViolation("brand_new_rule", "   ")).toBe("This transaction can't be disputed");
  });

  it("does not mistake an object property for a code", () => {
    expect(describeViolation("toString")).toBe("This transaction can't be disputed");
    expect(describeViolation("constructor")).toBe("This transaction can't be disputed");
  });
});

describe("describeBlockReasons", () => {
  it("is never empty and never repeats a sentence", () => {
    expect(describeBlockReasons([])).toEqual(["This transaction can't be disputed"]);
    expect(describeBlockReasons(["x", "y"])).toEqual(["This transaction can't be disputed"]);
    expect(describeBlockReasons(["not_posted", "not_debit"])).toHaveLength(2);
  });

  it("never uses the server's words for a block reason", () => {
    expect(describeBlockReasons(["<script>"])).toEqual(["This transaction can't be disputed"]);
  });
});
