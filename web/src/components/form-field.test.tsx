import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { CharacterCounter, FormField } from "@/components/form-field";

function field(props: { error?: string; hint?: string; optional?: boolean }) {
  render(
    <FormField id="f" label="Note" error={props.error} hint={props.hint} optional={props.optional}>
      {(described) => <input id="f" aria-describedby={described} />}
    </FormField>,
  );
  return screen.getByRole("textbox");
}

describe("FormField", () => {
  it("names the input and marks it required by default", () => {
    field({});
    expect(screen.getByLabelText(/^Note/)).toBe(screen.getByRole("textbox"));
    expect(screen.getByText("(required)")).toBeInTheDocument();
  });

  it("marks an optional field", () => {
    field({ optional: true });
    expect(screen.getByText("(optional)")).toBeInTheDocument();
    expect(screen.queryByText("(required)")).not.toBeInTheDocument();
  });

  it("describes the input by the hint, then the error", () => {
    const input = field({ hint: "Be brief", error: "Too long" });
    expect(input).toHaveAttribute("aria-describedby", "f-hint f-error");
    expect(input).toHaveAccessibleDescription("Be brief Too long");
    expect(screen.getByRole("alert")).toHaveTextContent("Too long");
  });

  it("describes nothing and shows no alert when there is no hint or error", () => {
    const input = field({});
    expect(input.getAttribute("aria-describedby")).toBe("");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});

describe("CharacterCounter", () => {
  it("shows the length against the maximum, red only over it", () => {
    const { rerender } = render(<CharacterCounter length={5} label="Length" />);
    expect(screen.getByText("5 / 2000")).not.toHaveClass("text-destructive");
    rerender(<CharacterCounter length={2001} label="Length" />);
    expect(screen.getByText("2001 / 2000")).toHaveClass("text-destructive");
  });

  it("has a polite, labelled live region", () => {
    render(<CharacterCounter length={5} label="Note length" />);
    expect(screen.getByRole("status", { name: "Note length" })).toHaveAttribute(
      "aria-live",
      "polite",
    );
  });
});
