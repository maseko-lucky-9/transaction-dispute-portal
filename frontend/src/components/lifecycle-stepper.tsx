import { Check, X } from "lucide-react";

import type { DisputeStatus } from "@/api/disputes";
import { stepperState, type StepperTone, type StepState } from "@/lib/stepper-state";
import { cn } from "@/lib/utils";

const STEP_SHAPE: Record<StepState, string> = {
  done: "bg-foreground text-card",
  current: "bg-card text-foreground ring-2 ring-foreground",
  upcoming: "bg-background text-muted-foreground ring-[1.5px] ring-border",
};

/** The last step takes the outcome colour once the dispute is decided. */
const LAST_DONE_TONE: Partial<Record<StepperTone, string>> = {
  accepted: "bg-status-accepted text-card",
  rejected: "bg-status-rejected text-card",
};

/**
 * Where a dispute is in its three-step life, as shapes only: no status words in the DOM (the
 * status pill carries those), just an accessible name on the image role. `reviewStarted` is
 * the history a withdrawn dispute needs to say which step it stopped at.
 */
export function LifecycleStepper({
  status,
  reviewStarted = false,
}: {
  status: DisputeStatus;
  reviewStarted?: boolean;
}) {
  const { steps, tone, label } = stepperState(status, reviewStarted);
  return (
    <span
      role="img"
      aria-label={label}
      className={cn("inline-flex items-center", tone === "withdrawn" && "opacity-45")}
    >
      {steps.map((state, index) => (
        <span key={index} className="inline-flex items-center">
          {index > 0 ? (
            <span
              aria-hidden="true"
              className={cn(
                "h-0.5 w-6",
                steps[index - 1] === "done" ? "bg-foreground" : "bg-border",
              )}
            />
          ) : null}
          <span
            aria-hidden="true"
            className={cn(
              "grid size-[22px] place-items-center rounded-full",
              STEP_SHAPE[state],
              index === 2 && state === "done" && LAST_DONE_TONE[tone],
            )}
          >
            {state === "done" ? (
              index === 2 && tone === "rejected" ? (
                <X className="size-3" strokeWidth={3} />
              ) : (
                <Check className="size-3" strokeWidth={3} />
              )
            ) : null}
          </span>
        </span>
      ))}
    </span>
  );
}
