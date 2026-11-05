import { describe, expect, it } from "vitest";

import { humanizeCode, reasonLabel } from "./dispute-reasons-catalogue";
import {
  DISPUTE_STATUS_LABELS,
  DISPUTE_STATUSES,
  disputesSearchSchema,
  WITHDRAWABLE_STATUSES,
} from "./dispute-filters";

describe("disputesSearchSchema (URL: never throws, falls back)", () => {
  const parse = (search: unknown) => disputesSearchSchema.parse(search);

  it("defaults limit to 25 and status to unset", () => {
    expect(parse({})).toEqual({ limit: 25 });
  });

  it.each(DISPUTE_STATUSES)("accepts status %s", (status) => {
    expect(parse({ status }).status).toBe(status);
  });

  it.each(["underreview", "UNDERREVIEW", "Hacked", "", 1, null, ["Accepted"], "Under review"])(
    "drops status %j",
    (status) => {
      expect(parse({ status }).status).toBeUndefined();
    },
  );

  it.each([1, 100, 50])("accepts limit %s", (limit) => {
    expect(parse({ limit }).limit).toBe(limit);
  });

  it.each([0, -1, 101, 2.5, "50", "abc", null, NaN])("falls back to 25 for limit %j", (limit) => {
    expect(parse({ limit }).limit).toBe(25);
  });

  it("ignores unknown keys", () => {
    expect(parse({ cursor: "abc", junk: 1 })).toEqual({ limit: 25 });
  });
});

describe("status words", () => {
  it("has a customer-facing label for every status, none of them a raw code", () => {
    for (const status of DISPUTE_STATUSES) expect(DISPUTE_STATUS_LABELS[status]).toBeTruthy();
    expect(DISPUTE_STATUS_LABELS.UnderReview).toBe("Under review");
  });

  it("allows withdrawal only before a decision", () => {
    expect(WITHDRAWABLE_STATUSES).toEqual(["Submitted", "UnderReview"]);
  });
});

describe("reason labels", () => {
  const catalogue = [{ code: "duplicate", label: "I was charged twice", description: "d" }];

  it("uses the catalogue's label", () => {
    expect(reasonLabel("duplicate", catalogue)).toBe("I was charged twice");
  });

  it("humanises a code the catalogue lacks, or when there is no catalogue", () => {
    expect(reasonLabel("not_received", catalogue)).toBe("Not received");
    expect(reasonLabel("not_received", undefined)).toBe("Not received");
    expect(humanizeCode("")).toBe("");
    expect(humanizeCode("a_b_c")).toBe("A b c");
  });
});
