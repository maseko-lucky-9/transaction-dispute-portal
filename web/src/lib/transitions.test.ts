import { describe, expect, it } from "vitest";

import { DISPUTE_STATUSES } from "./dispute-filters";
import { NEXT_TRANSITIONS, noteIsRequired } from "./transitions";

describe("NEXT_TRANSITIONS (mirrors the Domain's agent transitions)", () => {
  it("offers exactly the legal next states, in order", () => {
    expect(
      Object.fromEntries(
        DISPUTE_STATUSES.map((status) => [status, NEXT_TRANSITIONS[status].map((o) => o.to)]),
      ),
    ).toEqual({
      Submitted: ["UnderReview"],
      UnderReview: ["Accepted", "Rejected"],
      Accepted: [],
      Rejected: [],
      Withdrawn: [],
    });
  });

  it("never offers Withdrawn (that is the customer's) or a way back to Submitted", () => {
    const all = DISPUTE_STATUSES.flatMap((status) =>
      NEXT_TRANSITIONS[status].map((o) => o.to as string),
    );
    expect(all).not.toContain("Withdrawn");
    expect(all).not.toContain("Submitted");
  });

  it("starts a review with an optional note and no confirmation", () => {
    expect(NEXT_TRANSITIONS.Submitted[0]).toMatchObject({
      label: "Start review",
      noteRequired: false,
      confirm: false,
    });
  });

  it("requires a note for, and confirms, every decision", () => {
    for (const option of NEXT_TRANSITIONS.UnderReview) {
      expect(option).toMatchObject({ noteRequired: true, confirm: true });
    }
    expect(NEXT_TRANSITIONS.UnderReview.map((o) => o.label)).toEqual(["Accept", "Reject"]);
  });

  it("noteIsRequired follows the status", () => {
    expect(noteIsRequired("Submitted")).toBe(false);
    expect(noteIsRequired("UnderReview")).toBe(true);
    expect(noteIsRequired("Accepted")).toBe(false);
  });
});
