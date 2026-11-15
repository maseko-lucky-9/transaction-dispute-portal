import { describe, expect, it } from "vitest";

import { stepperState } from "./stepper-state";

describe("stepperState", () => {
  it.each([
    ["Submitted", false, ["current", "upcoming", "upcoming"], "none", "Step 1 of 3"],
    ["UnderReview", false, ["done", "current", "upcoming"], "none", "Step 2 of 3"],
    ["Accepted", false, ["done", "done", "done"], "accepted", "Step 3 of 3"],
    ["Rejected", false, ["done", "done", "done"], "rejected", "Step 3 of 3"],
    ["Withdrawn", false, ["done", "upcoming", "upcoming"], "withdrawn", "Stopped at step 1 of 3"],
    ["Withdrawn", true, ["done", "done", "upcoming"], "withdrawn", "Stopped at step 2 of 3"],
  ] as const)("%s (review started: %s)", (status, reviewStarted, steps, tone, label) => {
    expect(stepperState(status, reviewStarted)).toEqual({ steps, tone, label });
  });

  it("ignores the review history for every status but Withdrawn", () => {
    expect(stepperState("Submitted", true)).toEqual(stepperState("Submitted"));
    expect(stepperState("Accepted", true)).toEqual(stepperState("Accepted"));
  });
});
