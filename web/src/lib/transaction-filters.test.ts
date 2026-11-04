import { describe, expect, it } from "vitest";

import {
  DATE_RANGE_MESSAGES,
  dateRangeProblem,
  filterFormSchema,
  isIsoDate,
  parseIsoDate,
  transactionsSearchSchema,
} from "./transaction-filters";

describe("parseIsoDate / isIsoDate", () => {
  it.each(["2026-10-01", "2024-02-29", "2026-12-31", "0001-01-01"])("accepts %s", (value) => {
    expect(isIsoDate(value)).toBe(true);
  });

  it.each([
    ["a day that does not exist", "2026-02-30"],
    ["a leap day in a common year", "2026-02-29"],
    ["month 13", "2026-13-01"],
    ["month 00", "2026-00-10"],
    ["day 00", "2026-10-00"],
    ["no zero padding", "2026-1-1"],
    ["a timestamp", "2026-10-01T00:00:00Z"],
    ["leading space", " 2026-10-01"],
    ["slashes", "2026/10/01"],
    ["compact", "20261001"],
    ["empty", ""],
    ["words", "tomorrow"],
  ])("rejects %s (%s)", (_name, value) => {
    expect(isIsoDate(value)).toBe(false);
  });

  it("parses to UTC midnight", () => {
    expect(parseIsoDate("2026-10-01")?.toISOString()).toBe("2026-10-01T00:00:00.000Z");
  });
});

describe("dateRangeProblem", () => {
  it("accepts a single day, an ordered pair and open ends", () => {
    expect(dateRangeProblem("2026-10-01", "2026-10-01")).toBeNull();
    expect(dateRangeProblem("2026-10-01", "2026-10-31")).toBeNull();
    expect(dateRangeProblem("2026-10-01", undefined)).toBeNull();
    expect(dateRangeProblem(undefined, "2026-10-01")).toBeNull();
    expect(dateRangeProblem(undefined, undefined)).toBeNull();
  });

  it("rejects from after to", () => {
    expect(dateRangeProblem("2026-10-02", "2026-10-01")).toBe("order");
  });

  it("allows exactly 366 days and rejects 367", () => {
    // 2025-01-01 -> 2026-01-02 is 366 days; one more day is too long.
    expect(dateRangeProblem("2025-01-01", "2026-01-02")).toBeNull();
    expect(dateRangeProblem("2025-01-01", "2026-01-03")).toBe("range");
  });

  it("counts the leap day", () => {
    expect(dateRangeProblem("2024-01-01", "2025-01-01")).toBeNull(); // 366 days, 2024 is leap
    expect(dateRangeProblem("2024-01-01", "2025-01-02")).toBe("range");
  });

  it("ignores an end that is not a date (that is the field's own error)", () => {
    expect(dateRangeProblem("nope", "2026-10-01")).toBeNull();
    expect(dateRangeProblem("2026-10-01", "2026-02-30")).toBeNull();
  });
});

describe("transactionsSearchSchema (URL: never throws, falls back)", () => {
  const parse = (search: unknown) => transactionsSearchSchema.parse(search);

  it("defaults limit to 25 and everything else to unset", () => {
    expect(parse({})).toEqual({ limit: 25 });
  });

  it("keeps valid values", () => {
    expect(parse({ from: "2026-10-01", to: "2026-10-31", status: "Posted", limit: 50 })).toEqual({
      from: "2026-10-01",
      to: "2026-10-31",
      status: "Posted",
      limit: 50,
    });
  });

  it.each([1, 100, 25])("accepts limit %s", (limit) => {
    expect(parse({ limit }).limit).toBe(limit);
  });

  it.each([0, -1, 101, 2.5, "50", "abc", null, NaN, {}, []])(
    "falls back to 25 for limit %j",
    (limit) => {
      expect(parse({ limit }).limit).toBe(25);
    },
  );

  it.each(["Pending", "Posted", "Reversed"])("accepts status %s", (status) => {
    expect(parse({ status }).status).toBe(status);
  });

  it.each(["posted", "Hacked", "", 1, null, ["Posted"]])("drops status %j", (status) => {
    expect(parse({ status }).status).toBeUndefined();
  });

  it.each(["2026-02-30", "banana", 20261001, "", null, {}])("drops date %j", (value) => {
    expect(parse({ from: value, to: value })).toEqual({ limit: 25 });
  });

  it("keeps the valid end when the other is garbage", () => {
    expect(parse({ from: "banana", to: "2026-10-01" })).toEqual({ to: "2026-10-01", limit: 25 });
  });

  it("drops BOTH dates when from is after to", () => {
    expect(parse({ from: "2026-10-31", to: "2026-10-01", status: "Posted" })).toEqual({
      status: "Posted",
      limit: 25,
    });
  });

  it("drops BOTH dates when the range is over 366 days, keeps them at 366", () => {
    expect(parse({ from: "2025-01-01", to: "2026-01-03" })).toEqual({ limit: 25 });
    expect(parse({ from: "2025-01-01", to: "2026-01-02" })).toEqual({
      from: "2025-01-01",
      to: "2026-01-02",
      limit: 25,
    });
  });

  it("ignores unknown keys and survives non-object input shapes", () => {
    expect(parse({ junk: 1, cursor: "abc" })).toEqual({ limit: 25 });
  });
});

describe("filterFormSchema (form: fails with a message per field)", () => {
  const messagesFor = (values: unknown) => {
    const result = filterFormSchema.safeParse(values);
    if (result.success) return {};
    return Object.fromEntries(
      result.error.issues.map((issue) => [String(issue.path[0]), issue.message]),
    );
  };
  const empty = { from: "", to: "", status: "" };

  it("accepts an empty form (no filters)", () => {
    expect(filterFormSchema.safeParse(empty).success).toBe(true);
  });

  it("accepts a full valid form", () => {
    expect(
      filterFormSchema.safeParse({ from: "2026-10-01", to: "2026-10-31", status: "Reversed" })
        .success,
    ).toBe(true);
  });

  it("requires an ordered range", () => {
    expect(messagesFor({ ...empty, from: "2026-10-02", to: "2026-10-01" })).toEqual({
      to: DATE_RANGE_MESSAGES.order,
    });
  });

  it("caps the range at 366 days", () => {
    expect(messagesFor({ ...empty, from: "2025-01-01", to: "2026-01-03" })).toEqual({
      to: DATE_RANGE_MESSAGES.range,
    });
    expect(messagesFor({ ...empty, from: "2025-01-01", to: "2026-01-02" })).toEqual({});
  });

  it("rejects a malformed date on the field it belongs to", () => {
    expect(messagesFor({ ...empty, from: "2026-02-30" })).toEqual({ from: "Enter a valid date" });
    expect(messagesFor({ ...empty, to: "soon" })).toEqual({ to: "Enter a valid date" });
  });

  it("rejects a status outside the list", () => {
    expect(messagesFor({ ...empty, status: "Hacked" })).toEqual({
      status: "Choose a status from the list",
    });
  });

  it("reports each broken field at once", () => {
    expect(messagesFor({ from: "nope", to: "also nope", status: "x" })).toEqual({
      from: "Enter a valid date",
      to: "Enter a valid date",
      status: "Choose a status from the list",
    });
  });
});
