import { describe, expect, it } from "vitest";

import { DISPUTE_STATUSES } from "./dispute-filters";
import { NEXT_TRANSITIONS, noteIsRequired, optionsFor, RELEASE } from "./transitions";

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

  it("gives each status at most one crimson option, and Reject is the outlined one", () => {
    for (const status of DISPUTE_STATUSES) {
      const crimson = NEXT_TRANSITIONS[status].filter((o) => o.variant === "default");
      expect(crimson.length).toBeLessThanOrEqual(1);
    }
    expect(NEXT_TRANSITIONS.UnderReview.map((o) => [o.label, o.variant])).toEqual([
      ["Accept", "default"],
      ["Reject", "outline"],
    ]);
  });

  it("noteIsRequired follows the status", () => {
    expect(noteIsRequired("Submitted")).toBe(false);
    expect(noteIsRequired("UnderReview")).toBe(true);
    expect(noteIsRequired("Accepted")).toBe(false);
  });
});

describe("optionsFor (who is reviewing decides what this agent may do, ADR-019)", () => {
  const labels = (options: readonly { label: string }[]) => options.map((o) => o.label);

  it("the reviewing agent may accept, reject or release", () => {
    expect(labels(optionsFor("UnderReview", { kind: "mine", claimedAt: null }))).toEqual([
      "Accept",
      "Reject",
      "Release to queue",
    ]);
  });

  it("another agent's review offers nothing", () => {
    expect(
      optionsFor("UnderReview", { kind: "other", assignee: "bbbbbbbb", claimedAt: null }),
    ).toEqual([]);
  });

  it("an unknown reviewer (an older api) falls back to the status alone, with no release", () => {
    expect(labels(optionsFor("UnderReview", { kind: "unknown" }))).toEqual(["Accept", "Reject"]);
    expect(labels(optionsFor("Submitted", { kind: "unknown" }))).toEqual(["Start review"]);
  });

  it("an unclaimed dispute offers Start review, the claim, and a closed one nothing", () => {
    expect(labels(optionsFor("Submitted", { kind: "unclaimed" }))).toEqual(["Start review"]);
    expect(optionsFor("Accepted", { kind: "unknown" })).toEqual([]);
  });

  it("release goes back to Submitted, takes no note, is confirmed, and is never crimson", () => {
    expect(RELEASE).toMatchObject({
      to: "Submitted",
      takesNote: false,
      noteRequired: false,
      confirm: true,
      variant: "ghost",
    });
    const crimson = optionsFor("UnderReview", { kind: "mine", claimedAt: null }).filter(
      (o) => o.variant === "default",
    );
    expect(labels(crimson)).toEqual(["Accept"]);
  });
});
