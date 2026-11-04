import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { makeTransaction } from "@/test/transactions";

import { TransactionsTable } from "./transactions-table";

function rowFor(reference: string): HTMLElement {
  const row = screen.getByText(new RegExp(`Ref ${reference}$`)).closest("tr");
  if (!row) throw new Error(`no row for ${reference}`);
  return row;
}

describe("TransactionsTable semantics", () => {
  it("is a real table with a caption, column headers and row headers", () => {
    render(<TransactionsTable items={[makeTransaction({}, 0), makeTransaction({}, 1)]} />);

    const table = screen.getByRole("table", { name: "Your transactions, newest first" });
    const headers = within(table).getAllByRole("columnheader");
    expect(headers.map((h) => h.textContent)).toEqual([
      "Date (UTC)",
      "Merchant",
      "Account",
      "Amount",
      "Status",
      "Dispute",
    ]);
    for (const header of headers) expect(header).toHaveAttribute("scope", "col");

    const rowHeaders = within(table).getAllByRole("rowheader");
    expect(rowHeaders).toHaveLength(2);
    for (const header of rowHeaders) expect(header).toHaveAttribute("scope", "row");
  });

  it("makes the scrollable wrapper keyboard-reachable", () => {
    render(<TransactionsTable items={[makeTransaction()]} />);
    expect(screen.getByRole("region", { name: "Transactions table" })).toHaveAttribute(
      "tabindex",
      "0",
    );
  });
});

describe("TransactionsTable rows", () => {
  it("shows date, merchant, category, reference, account and status for a posted debit", () => {
    render(
      <TransactionsTable
        items={[
          makeTransaction({
            merchantName: "Checkers Sandton",
            category: "Groceries",
            reference: "REF-9",
            account: { maskedNumber: "**** 4821" },
            transactionDate: "2026-10-30T08:15:00Z",
          }),
        ]}
      />,
    );
    const row = within(rowFor("REF-9"));
    expect(row.getByText("30 Oct 2026")).toHaveAttribute("datetime", "2026-10-30T08:15:00Z");
    expect(row.getByText("Checkers Sandton")).toBeInTheDocument();
    expect(row.getByText(/Groceries/)).toBeInTheDocument();
    expect(row.getByText("**** 4821")).toBeInTheDocument();
    expect(row.getByText("Posted")).toBeInTheDocument();
  });

  it("shows a debit with a minus sign and the word Debit", () => {
    render(
      <TransactionsTable
        items={[makeTransaction({ amount: "123.45", direction: "Debit", reference: "D" })]}
      />,
    );
    const row = within(rowFor("D"));
    expect(row.getByText(/^−R\s123,45$/)).toBeInTheDocument();
    expect(row.getByText("Debit")).toBeInTheDocument();
    expect(row.queryByText("Credit")).not.toBeInTheDocument();
  });

  it("shows a credit with a plus sign and the word Credit, not colour alone", () => {
    render(
      <TransactionsTable
        items={[
          makeTransaction({
            amount: "500.00",
            direction: "Credit",
            reference: "C",
            isDisputable: false,
            disputeBlockReasons: ["not_debit"],
          }),
        ]}
      />,
    );
    const row = within(rowFor("C"));
    expect(row.getByText(/^\+R\s500,00$/)).toBeInTheDocument();
    expect(row.getByText("Credit")).toBeInTheDocument();
  });

  it("formats with the item's own currency", () => {
    render(
      <TransactionsTable
        items={[makeTransaction({ amount: "9.99", currency: "USD", reference: "U" })]}
      />,
    );
    expect(within(rowFor("U")).getByText(/US\$\s?9,99/)).toBeInTheDocument();
  });

  it("shows the dispute's status when one exists", () => {
    render(
      <TransactionsTable
        items={[makeTransaction({ reference: "X", dispute: { id: "d1", status: "UnderReview" } })]}
      />,
    );
    expect(within(rowFor("X")).getByText("Dispute: UnderReview")).toBeInTheDocument();
  });
});

describe("TransactionsTable dispute action", () => {
  it("explains why a pending transaction cannot be disputed", () => {
    render(
      <TransactionsTable
        items={[
          makeTransaction({
            reference: "P",
            merchantName: "Uber",
            status: "Pending",
            postedAt: null,
            isDisputable: false,
            disputeBlockReasons: ["not_posted"],
          }),
        ]}
      />,
    );
    const button = within(rowFor("P")).getByRole("button", { name: /^Dispute Uber/ });
    expect(button).toHaveAttribute("aria-disabled", "true");
    expect(button).toHaveAccessibleDescription("Pending transactions can't be disputed yet");
    expect(within(rowFor("P")).getByText("Pending")).toBeInTheDocument();
  });

  it("explains a credit and an expired window, each with its own sentence", () => {
    render(
      <TransactionsTable
        items={[
          makeTransaction(
            {
              reference: "CR",
              isDisputable: false,
              direction: "Credit",
              disputeBlockReasons: ["not_debit"],
            },
            0,
          ),
          makeTransaction(
            { reference: "OLD", isDisputable: false, disputeBlockReasons: ["window_expired"] },
            1,
          ),
        ]}
      />,
    );
    expect(within(rowFor("CR")).getByRole("button")).toHaveAccessibleDescription(
      "Only debits can be disputed",
    );
    expect(within(rowFor("OLD")).getByRole("button")).toHaveAccessibleDescription(
      "The 120-day dispute window has closed",
    );
  });

  it("lists every reason when there are several", () => {
    render(
      <TransactionsTable
        items={[
          makeTransaction({
            reference: "M",
            isDisputable: false,
            disputeBlockReasons: ["not_posted", "not_debit"],
          }),
        ]}
      />,
    );
    const description = within(rowFor("M")).getByRole("button").getAttribute("aria-describedby");
    const text = document.getElementById(description ?? "")?.textContent;
    expect(text).toContain("Pending transactions can't be disputed yet");
    expect(text).toContain("Only debits can be disputed");
  });

  it("stays focusable (so the reason is announced) but does nothing when activated", async () => {
    render(
      <TransactionsTable
        items={[
          makeTransaction({
            reference: "N",
            isDisputable: false,
            disputeBlockReasons: ["not_debit"],
          }),
        ]}
      />,
    );
    const button = within(rowFor("N")).getByRole("button");
    await userEvent.setup().tab(); // wrapper region
    await userEvent.setup().click(button);
    expect(button).not.toBeDisabled();
    expect(screen.queryByText("Coming in the next release")).not.toBeInTheDocument();
  });

  it("enables Dispute for an eligible row, and only announces the next release", async () => {
    render(
      <TransactionsTable items={[makeTransaction({ reference: "E", merchantName: "Netflix" })]} />,
    );
    const row = within(rowFor("E"));
    const button = row.getByRole("button", { name: /^Dispute Netflix/ });
    expect(button).not.toHaveAttribute("aria-disabled");
    expect(row.queryByText("Coming in the next release")).not.toBeInTheDocument();

    await userEvent.setup().click(button);

    expect(row.getByRole("status")).toHaveTextContent("Coming in the next release");
    expect(button).toHaveAccessibleDescription("Coming in the next release");
    expect(window.location.pathname).toBe("/"); // no navigation
  });
});
