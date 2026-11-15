import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { LifecycleStepper } from "./lifecycle-stepper";

describe("LifecycleStepper", () => {
  it("is an image with the step as its name and no text of its own", () => {
    render(<LifecycleStepper status="UnderReview" />);
    const stepper = screen.getByRole("img", { name: "Step 2 of 3" });
    expect(stepper).toHaveTextContent("");
    expect(stepper.querySelectorAll("svg")).toHaveLength(1);
  });

  it("ends a rejection with a cross and an acceptance with a tick", () => {
    const { container, rerender } = render(<LifecycleStepper status="Rejected" />);
    expect(container.querySelectorAll(".lucide-x")).toHaveLength(1);
    expect(container.querySelectorAll(".lucide-check")).toHaveLength(2);
    expect(Array.from(container.querySelectorAll("svg")).at(-1)).toHaveClass("lucide-x");
    rerender(<LifecycleStepper status="Accepted" />);
    expect(container.querySelectorAll(".lucide-x")).toHaveLength(0);
    expect(container.querySelectorAll(".lucide-check")).toHaveLength(3);
  });

  it("greys a withdrawn dispute and says where it stopped, using the history when given", () => {
    const { rerender } = render(<LifecycleStepper status="Withdrawn" />);
    expect(screen.getByRole("img", { name: "Stopped at step 1 of 3" })).toHaveClass("opacity-45");
    rerender(<LifecycleStepper status="Withdrawn" reviewStarted />);
    expect(screen.getByRole("img", { name: "Stopped at step 2 of 3" })).toHaveClass("opacity-45");
  });
});
