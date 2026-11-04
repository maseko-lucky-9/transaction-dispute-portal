import { describe, expect, it } from "vitest";

import { describeBlockReasons } from "./dispute-reasons";
import { formatDate, formatMoney } from "./format";

describe("formatMoney", () => {
  it("formats ZAR with the item's currency", () => {
    expect(formatMoney("1234.5", "ZAR")).toMatch(/^R\s1\s234,50$/);
  });

  it("uses the currency it is given, not a hard-coded one", () => {
    expect(formatMoney("5", "USD")).toMatch(/US\$\s?5,00/);
    expect(formatMoney("5", "EUR")).toMatch(/€\s?5,00/);
  });

  it("is exact for amounts a JS number cannot hold", () => {
    expect(formatMoney("12345678901234567.89", "ZAR")).toMatch(/567,89$/);
  });

  it("keeps cents and zero", () => {
    expect(formatMoney("0.05", "ZAR")).toMatch(/0,05$/);
    expect(formatMoney("0", "ZAR")).toMatch(/0,00$/);
  });

  it("shows the raw amount and code when Intl does not know the currency", () => {
    expect(formatMoney("12.30", "ZA")).toBe("12.30 ZA");
  });
});

describe("formatDate", () => {
  it("shows the UTC calendar day, not the viewer's", () => {
    expect(formatDate("2026-10-30T23:30:00Z")).toBe("30 Oct 2026");
    expect(formatDate("2026-10-31T00:30:00Z")).toBe("31 Oct 2026");
  });

  it("returns the input when it is not a date, rather than 'Invalid Date'", () => {
    expect(formatDate("not-a-date")).toBe("not-a-date");
  });
});

describe("describeBlockReasons", () => {
  it.each([
    ["not_posted", "Pending transactions can't be disputed yet"],
    ["not_debit", "Only debits can be disputed"],
    ["window_expired", "The 120-day dispute window has closed"],
  ])("maps %s", (reason, sentence) => {
    expect(describeBlockReasons([reason])).toEqual([sentence]);
  });

  it("keeps every reason, in order", () => {
    expect(describeBlockReasons(["not_debit", "window_expired"])).toEqual([
      "Only debits can be disputed",
      "The 120-day dispute window has closed",
    ]);
  });

  it("falls back for a reason this version does not know, and for none at all", () => {
    expect(describeBlockReasons(["something_new"])).toEqual(["This transaction can't be disputed"]);
    expect(describeBlockReasons([])).toEqual(["This transaction can't be disputed"]);
  });

  it("does not repeat a sentence", () => {
    expect(describeBlockReasons(["a", "b"])).toEqual(["This transaction can't be disputed"]);
  });

  it("does not treat Object.prototype members as reasons", () => {
    expect(describeBlockReasons(["constructor", "toString"])).toEqual([
      "This transaction can't be disputed",
    ]);
  });
});
