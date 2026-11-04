import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import type { TransactionItem } from "@/api/transactions";
import { fakeDisputesApi } from "@/test/disputes";
import { signedIn } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";
import { fakeTransactionsApi, makeTransaction } from "@/test/transactions";

/**
 * The table's Dispute actions are router links, so it is rendered where it lives: on the
 * transactions page, backed by the fake API. Assertions stay on the table and its rows.
 */
async function renderTable(items: TransactionItem[]) {
  const transactions = fakeTransactionsApi(items);
  server.use(
    signedIn(),
    ...transactions.handlers,
    ...fakeDisputesApi({ transactions: items }).handlers,
  );
  const view = renderApp("/transactions");
  await screen.findByRole("table");
  return view;
}

function rowFor(reference: string): HTMLElement {
  const row = screen.getByText(new RegExp(`Ref ${reference}$`)).closest("tr");
  if (!row) throw new Error(`no row for ${reference}`);
  return row;
}

describe("TransactionsTable semantics", () => {
  it("is a real table with a caption, column headers and row headers", async () => {
    await renderTable([makeTransaction({}, 0), makeTransaction({}, 1)]);

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

  it("makes the scrollable wrapper keyboard-reachable", async () => {
    await renderTable([makeTransaction()]);
    expect(screen.getByRole("region", { name: "Transactions table" })).toHaveAttribute(
      "tabindex",
      "0",
    );
  });
});

describe("TransactionsTable rows", () => {
  it("shows date, merchant, category, reference, account and status for a posted debit", async () => {
    await renderTable([
      makeTransaction({
        merchantName: "Checkers Sandton",
        category: "Groceries",
        reference: "REF-9",
        account: { maskedNumber: "**** 4821" },
        transactionDate: "2026-10-30T08:15:00Z",
      }),
    ]);
    const row = within(rowFor("REF-9"));
    expect(row.getByText("30 Oct 2026")).toHaveAttribute("datetime", "2026-10-30T08:15:00Z");
    expect(row.getByText("Checkers Sandton")).toBeInTheDocument();
    expect(row.getByText(/Groceries/)).toBeInTheDocument();
    expect(row.getByText("**** 4821")).toBeInTheDocument();
    expect(row.getByText("Posted")).toBeInTheDocument();
  });

  it("shows a debit with a minus sign and the word Debit", async () => {
    await renderTable([makeTransaction({ amount: "123.45", direction: "Debit", reference: "D" })]);
    const row = within(rowFor("D"));
    expect(row.getByText(/^−R\s123,45$/)).toBeInTheDocument();
    expect(row.getByText("Debit")).toBeInTheDocument();
    expect(row.queryByText("Credit")).not.toBeInTheDocument();
  });

  it("shows a credit with a plus sign and the word Credit, not colour alone", async () => {
    await renderTable([
      makeTransaction({
        amount: "500.00",
        direction: "Credit",
        reference: "C",
        isDisputable: false,
        disputeBlockReasons: ["not_debit"],
      }),
    ]);
    const row = within(rowFor("C"));
    expect(row.getByText(/^\+R\s500,00$/)).toBeInTheDocument();
    expect(row.getByText("Credit")).toBeInTheDocument();
  });

  it("formats with the item's own currency", async () => {
    await renderTable([makeTransaction({ amount: "9.99", currency: "USD", reference: "U" })]);
    expect(within(rowFor("U")).getByText(/US\$\s?9,99/)).toBeInTheDocument();
  });

  it("shows the dispute's status when one exists", async () => {
    await renderTable([
      makeTransaction({ reference: "X", dispute: { id: "d1", status: "UnderReview" } }),
    ]);
    expect(within(rowFor("X")).getByText("Dispute: UnderReview")).toBeInTheDocument();
  });
});

describe("TransactionsTable dispute action", () => {
  it("explains why a pending transaction cannot be disputed", async () => {
    await renderTable([
      makeTransaction({
        reference: "P",
        merchantName: "Uber",
        status: "Pending",
        postedAt: null,
        isDisputable: false,
        disputeBlockReasons: ["not_posted"],
      }),
    ]);
    const button = within(rowFor("P")).getByRole("button", { name: /^Dispute Uber/ });
    expect(button).toHaveAttribute("aria-disabled", "true");
    expect(button).toHaveAccessibleDescription("Pending transactions can't be disputed yet");
    expect(within(rowFor("P")).getByText("Pending")).toBeInTheDocument();
  });

  it("explains a credit and an expired window, each with its own sentence", async () => {
    await renderTable([
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
    ]);
    expect(within(rowFor("CR")).getByRole("button")).toHaveAccessibleDescription(
      "Only debits can be disputed",
    );
    expect(within(rowFor("OLD")).getByRole("button")).toHaveAccessibleDescription(
      "The 120-day dispute window has closed",
    );
  });

  it("lists every reason when there are several", async () => {
    await renderTable([
      makeTransaction({
        reference: "M",
        isDisputable: false,
        disputeBlockReasons: ["not_posted", "not_debit"],
      }),
    ]);
    const description = within(rowFor("M")).getByRole("button").getAttribute("aria-describedby");
    const text = document.getElementById(description ?? "")?.textContent;
    expect(text).toContain("Pending transactions can't be disputed yet");
    expect(text).toContain("Only debits can be disputed");
  });

  it("stays focusable (so the reason is announced) but goes nowhere when activated", async () => {
    const { router } = await renderTable([
      makeTransaction({ reference: "N", isDisputable: false, disputeBlockReasons: ["not_debit"] }),
    ]);
    const button = within(rowFor("N")).getByRole("button");
    await userEvent.setup().click(button);

    expect(button).not.toBeDisabled();
    expect(within(rowFor("N")).queryByRole("link")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/transactions");
  });

  it("makes Dispute a real link to the form for an eligible row", async () => {
    const item = makeTransaction({ reference: "E", merchantName: "Netflix" });
    const { router } = await renderTable([item]);
    const link = within(rowFor("E")).getByRole("link", { name: /^Dispute Netflix, / });

    expect(link).toHaveAttribute("href", `/transactions/${item.id}/dispute`);
    expect(link).not.toHaveAttribute("aria-disabled");
    expect(within(rowFor("E")).queryByRole("button")).not.toBeInTheDocument();

    await userEvent.setup().click(link);

    expect(router.state.location.pathname).toBe(`/transactions/${item.id}/dispute`);
    expect(
      await screen.findByRole("heading", { level: 1, name: "Dispute a transaction" }),
    ).toBeInTheDocument();
  });

  it("says a transaction already has a dispute, and links to it", async () => {
    await renderTable([
      makeTransaction({
        reference: "AD",
        merchantName: "Takealot",
        isDisputable: false,
        disputeBlockReasons: ["already_disputed"],
        dispute: { id: "dddddddd-0000-4000-8000-000000000001", status: "UnderReview" },
      }),
    ]);
    const row = within(rowFor("AD"));

    expect(row.getByRole("button", { name: /^Dispute Takealot/ })).toHaveAccessibleDescription(
      "This transaction already has a dispute",
    );
    expect(row.getByRole("link", { name: /^View dispute for Takealot/ })).toHaveAttribute(
      "href",
      "/disputes/dddddddd-0000-4000-8000-000000000001",
    );
  });

  it("offers no link when it has no dispute to point at", async () => {
    await renderTable([
      makeTransaction({
        reference: "NA",
        isDisputable: false,
        disputeBlockReasons: ["already_disputed"],
      }),
    ]);
    expect(within(rowFor("NA")).queryByRole("link")).not.toBeInTheDocument();
    expect(within(rowFor("NA")).getByRole("button")).toHaveAccessibleDescription(
      "This transaction already has a dispute",
    );
  });
});
