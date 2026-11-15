import type { DisputeStatus } from "@/api/disputes";

export type StepState = "done" | "current" | "upcoming";
export type StepperTone = "none" | "accepted" | "rejected" | "withdrawn";

export interface StepperState {
  readonly steps: readonly [StepState, StepState, StepState];
  readonly tone: StepperTone;
  /** The only words the stepper carries: its accessible name (it has no visible text). */
  readonly label: string;
}

/**
 * The three-step lifecycle (Submitted, Under review, Decided) as shape states. `reviewStarted`
 * only matters for Withdrawn: a withdrawn dispute stopped at step 2 if review had begun, else
 * at step 1. Callers without the history (the queue row) leave it false.
 */
export function stepperState(status: DisputeStatus, reviewStarted = false): StepperState {
  switch (status) {
    case "Submitted":
      return { steps: ["current", "upcoming", "upcoming"], tone: "none", label: "Step 1 of 3" };
    case "UnderReview":
      return { steps: ["done", "current", "upcoming"], tone: "none", label: "Step 2 of 3" };
    case "Accepted":
      return { steps: ["done", "done", "done"], tone: "accepted", label: "Step 3 of 3" };
    case "Rejected":
      return { steps: ["done", "done", "done"], tone: "rejected", label: "Step 3 of 3" };
    case "Withdrawn":
      return reviewStarted
        ? {
            steps: ["done", "done", "upcoming"],
            tone: "withdrawn",
            label: "Stopped at step 2 of 3",
          }
        : {
            steps: ["done", "upcoming", "upcoming"],
            tone: "withdrawn",
            label: "Stopped at step 1 of 3",
          };
  }
}
