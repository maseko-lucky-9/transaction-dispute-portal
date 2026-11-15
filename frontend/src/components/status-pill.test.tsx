import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";

import { DisputeStatusPill, TransactionStatusPill } from "@/components/status-pill";
import { DISPUTE_STATUSES, DISPUTE_STATUS_LABELS } from "@/lib/dispute-filters";
import { DISPUTE_STATUS_STYLES, TRANSACTION_STATUS_STYLES } from "@/lib/status-styles";
import { TRANSACTION_STATUSES, TRANSACTION_STATUS_LABELS } from "@/lib/transaction-filters";

const DISPUTE_EXPECTED = {
  Submitted: ["Submitted", "submitted"],
  UnderReview: ["Under review", "review"],
  Accepted: ["Accepted", "accepted"],
  Rejected: ["Rejected", "rejected"],
  Withdrawn: ["Withdrawn", "withdrawn"],
} as const;

const TRANSACTION_EXPECTED = {
  Pending: ["Pending", "review"],
  Posted: ["Posted", "accepted"],
  Reversed: ["Reversed", "rejected"],
} as const;

describe("status pills", () => {
  it.each(DISPUTE_STATUSES)("dispute %s has its label, colour and an icon", (status) => {
    const [label, variant] = DISPUTE_EXPECTED[status];
    expect(DISPUTE_STATUS_LABELS[status]).toBe(label);
    expect(DISPUTE_STATUS_STYLES[status].variant).toBe(variant);

    const { container } = render(<DisputeStatusPill status={status} />);
    const pill = screen.getByText(label);
    expect(pill).toHaveAttribute("data-status", status);
    expect(pill.className).toContain(`text-status-${variant}`);
    expect(pill.className).toContain(`bg-status-${variant}-bg`);
    const icon = container.querySelector("svg");
    expect(icon).toHaveAttribute("aria-hidden", "true");
    expect(icon?.textContent).toBe("");
  });

  it.each(TRANSACTION_STATUSES)("transaction %s has its label, colour and an icon", (status) => {
    const [label, variant] = TRANSACTION_EXPECTED[status];
    expect(TRANSACTION_STATUS_LABELS[status]).toBe(label);
    expect(TRANSACTION_STATUS_STYLES[status].variant).toBe(variant);

    const { container } = render(<TransactionStatusPill status={status} />);
    const pill = screen.getByText(label);
    expect(pill).toHaveAttribute("data-status", status);
    expect(pill.className).toContain(`text-status-${variant}`);
    expect(container.querySelector("svg")).toHaveAttribute("aria-hidden", "true");
  });

  it("prefixes the label as one text node", () => {
    render(<DisputeStatusPill status="UnderReview" prefix="Dispute" />);
    expect(screen.getByText("Dispute: Under review")).toBeInTheDocument();
  });
});
