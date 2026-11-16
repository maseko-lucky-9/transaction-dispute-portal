import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse, type HttpHandler } from "msw";
import { describe, expect, it } from "vitest";

import type { AgentCustomerTransaction } from "@/api/agent";
import { createQueryClient } from "@/api/query-client";
import { AgentCustomerTransactions } from "@/components/agent-customer-transactions";
import { fakeAgentApi, makeAgentCustomerTransaction, makeAgentDispute } from "@/test/agent";
import { bob, problem, signedIn } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";

const ID = "dddddddd-0000-4000-8000-000000000001";
/** The transaction the fake dispute is about (`makeAgentDispute`'s transactionId). */
const TXN = "00000000-0000-4000-8000-000000000001";
const LIST_URL = `/api/v1/agent/disputes/${ID}/customer-transactions`;

/** The agent dispute page, where the list lives, with `items` as the customer's transactions. */
async function openDispute(items: AgentCustomerTransaction[], first: HttpHandler[] = []) {
  const fake = fakeAgentApi([makeAgentDispute({ status: "UnderReview" })], items);
  server.use(signedIn(bob), ...first, ...fake.handlers);
  const view = renderApp(`/agent/disputes/${ID}`);
  await screen.findByRole("heading", { level: 1, name: /^Dispute: Merchant 0001/ });
  return { ...fake, ...view };
}

/** The next read of the list fails once; the one after it is the fake's. */
const failOnce = () =>
  http.get(LIST_URL, () => problem(503, { code: "idp_unavailable" }), { once: true });

const section = () => screen.findByRole("region", { name: "Customer transactions list" });

const many = (count: number) =>
  Array.from({ length: count }, (_, index) => makeAgentCustomerTransaction({}, index));

function rowOf(merchant: string): HTMLElement {
  const row = screen.getByText(merchant).closest("tr");
  if (!row) throw new Error(`no row for ${merchant}`);
  return row;
}

describe("agent customer transactions", () => {
  it("lists the customer's transactions newest first, since the date the server gives", async () => {
    await openDispute(many(3));

    const list = await section();
    expect(
      await screen.findByRole("heading", { level: 2, name: "Transactions since 04 Aug 2026" }),
    ).toBeInTheDocument();
    const table = within(list).getByRole("table", {
      name: "The customer's transactions, newest first",
    });
    const merchants = within(table)
      .getAllByRole("rowheader")
      .map((header) => header.textContent);
    expect(merchants).toEqual([
      "Shop 0001Groceries · Ref TXN-0001",
      "Shop 0002Groceries · Ref TXN-0002",
      "Shop 0003Groceries · Ref TXN-0003",
    ]);
    expect(
      within(table)
        .getAllByRole("columnheader")
        .map((header) => header.textContent),
    ).toEqual(["Date (UTC)", "Merchant", "Account", "Amount", "Status"]);
    expect(within(rowOf("Shop 0001")).getByText("03 Nov 2026")).toBeInTheDocument();
  });

  it("shows only the masked account, and reads the list without a customer or a limit", async () => {
    const { api } = await openDispute(many(2));
    await section();
    await screen.findByText("Shop 0001");

    expect(screen.getAllByText("**** 7788")).toHaveLength(2);
    // The customer comes from the dispute on the server: the one request names the dispute only.
    expect(api.customerTransactionRequests).toHaveLength(1);
    expect(api.customerTransactionRequests[0]?.id).toBe(ID);
    expect(Object.fromEntries(api.customerTransactionRequests[0]!.url.searchParams)).toEqual({});
  });

  it("labels a credit with its sign and the word Credit, and a debit with its sign and Debit", async () => {
    await openDispute([
      makeAgentCustomerTransaction({ amount: "1234.5" }, 0),
      makeAgentCustomerTransaction({ direction: "Credit", amount: "50.00" }, 1),
    ]);
    await screen.findByText("Shop 0001");

    const debit = rowOf("Shop 0001");
    expect(within(debit).getByText(/^−/)).toHaveTextContent("−R 1 234,50");
    expect(within(debit).getByText("Debit")).toBeInTheDocument();
    expect(within(debit).queryByText("Credit")).not.toBeInTheDocument();
    const credit = rowOf("Shop 0002");
    expect(within(credit).getByText(/^\+/)).toHaveTextContent("+R 50,00");
    expect(within(credit).getByText(/^\+/)).toHaveClass("text-positive");
    expect(within(credit).getByText("Credit")).toBeInTheDocument();
  });

  it("names this dispute's row in words and every other dispute by its status", async () => {
    await openDispute([
      makeAgentCustomerTransaction({ id: TXN, dispute: { id: ID, status: "UnderReview" } }, 0),
      makeAgentCustomerTransaction(
        { dispute: { id: "ffffffff-0000-4000-8000-000000000009", status: "Withdrawn" } },
        1,
      ),
      makeAgentCustomerTransaction({}, 2),
    ]);
    await screen.findByText("Shop 0001");

    const mine = rowOf("Shop 0001");
    expect(within(mine).getByText("This dispute")).toBeInTheDocument();
    expect(within(mine).getByText("Dispute: Under review")).toBeInTheDocument();
    const other = rowOf("Shop 0002");
    expect(within(other).queryByText("This dispute")).not.toBeInTheDocument();
    expect(within(other).getByText("Dispute: Withdrawn")).toBeInTheDocument();
    const none = rowOf("Shop 0003");
    expect(within(none).queryByText("This dispute")).not.toBeInTheDocument();
    expect(within(none).queryByText(/^Dispute:/)).not.toBeInTheDocument();
    expect(screen.getAllByText("This dispute")).toHaveLength(1);
  });

  it("gives every status pill its word, and offers nothing that acts on a transaction", async () => {
    await openDispute([
      makeAgentCustomerTransaction(
        { id: TXN, status: "Posted", dispute: { id: ID, status: "UnderReview" } },
        0,
      ),
      makeAgentCustomerTransaction({ status: "Pending", postedAt: null }, 1),
      makeAgentCustomerTransaction({ status: "Reversed" }, 2),
    ]);
    const table = within(await section()).getByRole("table");

    const pills = Array.from(table.querySelectorAll("[data-status]"));
    expect(pills.map((pill) => pill.textContent)).toEqual([
      "Posted",
      "Dispute: Under review",
      "Pending",
      "Reversed",
    ]);
    expect(within(table).queryAllByRole("button")).toEqual([]);
    expect(within(table).queryAllByRole("link")).toEqual([]);
  });

  it("says so, kindly, when there is nothing to show", async () => {
    await openDispute([]);

    const list = await section();
    expect(
      await within(list).findByText("There are no transactions to show for this period"),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Transactions since 04 Aug 2026" }),
    ).toBeInTheDocument();
    expect(within(list).queryByRole("table")).not.toBeInTheDocument();
  });

  it("shows an error with a retry when the first page fails, and the decision is still there", async () => {
    await openDispute([makeAgentCustomerTransaction({}, 0)], [failOnce()]);
    const user = userEvent.setup();

    const alert = await within(await section()).findByRole("alert");
    expect(
      within(alert).getByText("We could not load this customer's transactions"),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Customer transactions" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Accept" })).toBeInTheDocument();

    await user.click(within(alert).getByRole("button", { name: "Try again" }));

    expect(await screen.findByText("Shop 0001")).toBeInTheDocument();
    expect(
      screen.queryByText("We could not load this customer's transactions"),
    ).not.toBeInTheDocument();
  });

  it("loads more with the cursor it was given, and keeps the rows when the next page fails", async () => {
    const { api } = await openDispute(many(30));
    await screen.findByText("Shop 0025");
    const user = userEvent.setup();
    expect(screen.queryByText("Shop 0026")).not.toBeInTheDocument();

    server.use(failOnce());
    await user.click(screen.getByRole("button", { name: "Load more" }));

    const alert = await screen.findByText("We could not load more transactions");
    expect(alert).toBeInTheDocument();
    // The rows already read stay.
    expect(screen.getByText("Shop 0025")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByText("Shop 0030")).toBeInTheDocument();
    expect(screen.queryByText("We could not load more transactions")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Load more" })).not.toBeInTheDocument();
    const cursors = api.customerTransactionRequests.map((request) =>
      request.url.searchParams.get("cursor"),
    );
    expect(cursors[0]).toBeNull();
    expect(cursors.at(-1)).toEqual(expect.stringMatching(/\S/));
    expect(api.customerTransactionRequests.every((request) => request.id === ID)).toBe(true);
    await waitFor(() =>
      expect(within(screen.getByRole("table")).getAllByRole("row")).toHaveLength(31),
    );
  });

  // D1 was withdrawn and D2 raised on the same transaction: its row now shows D2, but it is still the one D1 is about.
  it("marks the disputed transaction's row even when a later dispute has replaced this one on it", async () => {
    const D1 = "dddddddd-0000-4000-8000-0000000000d1";
    const D2 = "dddddddd-0000-4000-8000-0000000000d2";
    server.use(
      http.get(`/api/v1/agent/disputes/${D1}/customer-transactions`, () =>
        HttpResponse.json({
          items: [
            makeAgentCustomerTransaction({ dispute: { id: D2, status: "UnderReview" } }, 0),
            makeAgentCustomerTransaction({ id: TXN, dispute: { id: D2, status: "Submitted" } }, 1),
            makeAgentCustomerTransaction({ dispute: { id: D1, status: "Withdrawn" } }, 2),
          ],
          nextCursor: null,
          from: "2026-08-04",
        }),
      ),
    );
    const queryClient = createQueryClient();
    render(
      <QueryClientProvider client={queryClient}>
        <AgentCustomerTransactions disputeId={D1} transactionId={TXN} />
      </QueryClientProvider>,
    );

    await screen.findByText("Shop 0002");

    // Only the row of the disputed transaction, though its latest dispute is D2.
    const marked = rowOf("Shop 0002");
    expect(within(marked).getByText("This dispute")).toBeInTheDocument();
    expect(within(marked).getByText("Dispute: Submitted")).toBeInTheDocument();
    expect(screen.getAllByText("This dispute")).toHaveLength(1);
    expect(within(rowOf("Shop 0003")).queryByText("This dispute")).not.toBeInTheDocument();
  });
});
