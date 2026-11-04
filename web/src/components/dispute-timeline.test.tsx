import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { TimelineEntry } from "@/api/disputes";

import { DisputeTimeline } from "./dispute-timeline";

const entry = (overrides: Partial<TimelineEntry>): TimelineEntry => ({
  from: null,
  to: "Submitted",
  actorType: "Customer",
  note: null,
  occurredAt: "2026-11-04T09:00:00Z",
  ...overrides,
});

const items = () => within(screen.getByRole("list", { name: "Timeline" })).getAllByRole("listitem");

describe("DisputeTimeline", () => {
  it("is an ordered list, oldest first, each entry with a machine-readable time", () => {
    render(
      <DisputeTimeline
        entries={[
          entry({}),
          entry({
            from: "Submitted",
            to: "UnderReview",
            actorType: "Agent",
            occurredAt: "2026-11-05T10:30:00Z",
          }),
        ]}
      />,
    );

    expect(screen.getByRole("list", { name: "Timeline" }).tagName).toBe("OL");
    expect(items()).toHaveLength(2);
    expect(items()[0]).toHaveTextContent("4 Nov 2026, 09:00 UTC");
    expect(items()[1]).toHaveTextContent("5 Nov 2026, 10:30 UTC");
    const times = [...document.querySelectorAll("time")].map((t) => t.dateTime);
    expect(times).toEqual(["2026-11-04T09:00:00Z", "2026-11-05T10:30:00Z"]);
  });

  it("says 'You' for the customer and 'Back-office' for an agent, never a name", () => {
    render(
      <DisputeTimeline
        entries={[
          entry({}),
          entry({ from: "Submitted", to: "UnderReview", actorType: "Agent" }),
          entry({ from: "UnderReview", to: "Accepted", actorType: "Agent" }),
        ]}
      />,
    );
    expect(items()[0]).toHaveTextContent("You submitted this dispute");
    expect(items()[1]).toHaveTextContent(
      "Back-office changed the status from Submitted to Under review",
    );
    expect(items()[2]).toHaveTextContent(
      "Back-office changed the status from Under review to Accepted",
    );
  });

  it("words a customer withdrawal naturally", () => {
    render(<DisputeTimeline entries={[entry({ from: "Submitted", to: "Withdrawn" })]} />);
    expect(items()[0]).toHaveTextContent("You withdrew this dispute");
  });

  it("covers the less common shapes without inventing an actor", () => {
    render(
      <DisputeTimeline
        entries={[
          entry({ from: null, to: "UnderReview", actorType: "Agent" }),
          entry({ from: "UnderReview", to: "Submitted", actorType: "Customer" }),
        ]}
      />,
    );
    expect(items()[0]).toHaveTextContent("Back-office set the status to Under review");
    expect(items()[1]).toHaveTextContent("You changed the status from Under review to Submitted");
  });

  it("shows a note when there is one, and nothing when there is not", () => {
    render(
      <DisputeTimeline
        entries={[
          entry({ note: null }),
          entry({
            from: "Submitted",
            to: "Rejected",
            actorType: "Agent",
            note: "No evidence of a duplicate charge.",
          }),
        ]}
      />,
    );
    expect(items()[0]).not.toHaveTextContent("Note:");
    expect(items()[1]).toHaveTextContent("Note: No evidence of a duplicate charge.");
  });

  it("renders a note as text, not markup", () => {
    render(<DisputeTimeline entries={[entry({ note: "<img src=x onerror=alert(1)>" })]} />);
    expect(document.querySelector("img")).toBeNull();
    expect(items()[0]).toHaveTextContent("<img src=x onerror=alert(1)>");
  });
});
