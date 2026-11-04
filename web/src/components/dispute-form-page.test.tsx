import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";

import type { CreateDisputeRequest } from "@/api/disputes";
import type { TransactionItem } from "@/api/transactions";
import { INVALID_CHARACTERS_MESSAGE, SENSITIVE_MESSAGE } from "@/lib/dispute-form";
import cases from "@/lib/validation-cases.json";
import { fakeDisputesApi, makeDisputeDetail } from "@/test/disputes";
import { problem, signedIn } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";
import { fakeTransactionsApi, makeTransaction } from "@/test/transactions";

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const GOOD_TEXT = "I was charged twice for the same order.";

type FakeOptions = NonNullable<Parameters<typeof fakeDisputesApi>[0]>;

async function openForm(
  overrides: Partial<TransactionItem> = {},
  options: FakeOptions = {},
  { strict = false }: { strict?: boolean } = {},
) {
  const tx = makeTransaction({ merchantName: "Netflix", amount: "123.45", ...overrides });
  const transactions = fakeTransactionsApi([tx]);
  const disputes = fakeDisputesApi({ transactions: [tx], ...options });
  server.use(signedIn(), ...transactions.handlers, ...disputes.handlers);
  const view = renderApp(`/transactions/${tx.id}/dispute`, { strict });
  await screen.findByRole("form", { name: "Dispute this transaction" });
  return { tx, api: disputes.api, ...view, user: userEvent.setup() };
}

type User = ReturnType<typeof userEvent.setup>;

const reasonSelect = () => screen.getByLabelText(/^Reason/);
const descriptionBox = () => screen.getByLabelText(/^What happened/);
const amountBox = () => screen.getByLabelText(/^Amount to dispute/);
const submitButton = () => screen.getByRole("button", { name: /^(Submit dispute|Submitting)/ });

async function write(user: User, element: HTMLElement, text: string) {
  await user.click(element);
  await user.paste(text);
}

async function fillValid(
  user: User,
  { reason = "duplicate", description = GOOD_TEXT, amount = "" } = {},
) {
  await user.selectOptions(reasonSelect(), reason);
  await write(user, descriptionBox(), description);
  if (amount) await write(user, amountBox(), amount);
}

function sent(api: { createBodies: CreateDisputeRequest[] }, index = 0): CreateDisputeRequest {
  const body = api.createBodies[index];
  if (!body) throw new Error(`no create request #${index + 1}`);
  return body;
}

const errorText = (element: HTMLElement) => {
  const ids = element.getAttribute("aria-describedby")?.split(" ") ?? [];
  return ids
    .map((id) => document.getElementById(id))
    .filter((el) => el?.getAttribute("role") === "alert")
    .map((el) => el?.textContent)
    .join(" ");
};

describe("dispute form page: loading", () => {
  it("shows the transaction, the reasons and a form with labelled fields", async () => {
    const { tx } = await openForm({ merchantName: "Woolworths", reference: "REF-77" });

    expect(
      screen.getByRole("heading", { level: 1, name: "Dispute a transaction" }),
    ).toBeInTheDocument();
    const summary = within(screen.getByRole("region", { name: "The transaction" }));
    expect(summary.getByText("Woolworths")).toBeInTheDocument();
    expect(summary.getByText("REF-77")).toBeInTheDocument();
    expect(summary.getByText("Amount").nextElementSibling).toHaveTextContent(/^R\s123,45$/);

    expect(reasonSelect()).toHaveAccessibleName("Reason (required)");
    expect(descriptionBox()).toHaveAccessibleName("What happened? (required)");
    expect(amountBox()).toHaveAccessibleName("Amount to dispute (optional)");
    const options = within(reasonSelect())
      .getAllByRole("option")
      .map((o) => o.textContent);
    expect(options).toEqual([
      "Choose a reason",
      "I did not make this purchase",
      "I was charged twice",
      "I never received the goods or service",
    ]);
    // The maximum is stated up front, not only when it is exceeded.
    expect(amountBox()).toHaveAccessibleDescription(/R\s123,45, the most you can dispute/);
    expect(document.title).toBe("Dispute a transaction - Transactions Dispute Portal");
    expect(tx.id).toBeTruthy();
  });

  it("describes the chosen reason to assistive technology", async () => {
    const { user } = await openForm();
    expect(reasonSelect()).not.toHaveAccessibleDescription();

    await user.selectOptions(reasonSelect(), "duplicate");

    expect(reasonSelect()).toHaveAccessibleDescription("The same purchase appears more than once.");
  });

  it.each(["not-a-guid", "123", "../disputes"])(
    "does not ask the API about the malformed id %j",
    async (id) => {
      const transactions = fakeTransactionsApi([]);
      const disputes = fakeDisputesApi();
      server.use(signedIn(), ...transactions.handlers, ...disputes.handlers);
      renderApp(`/transactions/${encodeURIComponent(id)}/dispute`);

      expect(
        await screen.findByRole("heading", { name: "Transaction not found" }),
      ).toBeInTheDocument();
      expect(transactions.api.requests).toHaveLength(0);
      expect(disputes.api.requests).toHaveLength(0);
    },
  );

  it("says so for a transaction that is not yours or does not exist (404)", async () => {
    server.use(signedIn(), ...fakeTransactionsApi([]).handlers, ...fakeDisputesApi().handlers);
    renderApp("/transactions/aaaaaaaa-0000-4000-8000-000000000999/dispute");

    expect(
      await screen.findByRole("heading", { name: "Transaction not found" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back to my transactions" })).toHaveAttribute(
      "href",
      "/transactions",
    );
    expect(screen.queryByRole("form")).not.toBeInTheDocument();
  });

  it("explains instead of showing a form that is certain to be refused", async () => {
    const tx = makeTransaction({
      status: "Pending",
      postedAt: null,
      isDisputable: false,
      disputeBlockReasons: ["not_posted", "window_expired"],
    });
    server.use(signedIn(), ...fakeTransactionsApi([tx]).handlers, ...fakeDisputesApi().handlers);
    renderApp(`/transactions/${tx.id}/dispute`);

    const section = await screen.findByRole("region", {
      name: "This transaction can't be disputed",
    });
    expect(
      within(section).getByText("Pending transactions can't be disputed yet"),
    ).toBeInTheDocument();
    expect(within(section).getByText("The 120-day dispute window has closed")).toBeInTheDocument();
    expect(screen.queryByRole("form")).not.toBeInTheDocument();
  });

  it("points a transaction that already has a dispute at that dispute", async () => {
    const tx = makeTransaction({
      isDisputable: false,
      disputeBlockReasons: ["already_disputed"],
      dispute: { id: "dddddddd-0000-4000-8000-000000000001", status: "Submitted" },
    });
    server.use(signedIn(), ...fakeTransactionsApi([tx]).handlers, ...fakeDisputesApi().handlers);
    renderApp(`/transactions/${tx.id}/dispute`);

    const section = await screen.findByRole("region", {
      name: "This transaction can't be disputed",
    });
    expect(within(section).getByText("This transaction already has a dispute")).toBeInTheDocument();
    expect(within(section).getByRole("link", { name: "View the dispute" })).toHaveAttribute(
      "href",
      "/disputes/dddddddd-0000-4000-8000-000000000001",
    );
  });

  it("shows a failure to load the transaction with its reference, and recovers on retry", async () => {
    const tx = makeTransaction();
    let fail = true;
    server.use(
      // First, so it wins while `fail` is set and then steps aside (undefined falls through).
      http.get("/api/v1/transactions/:id", () =>
        fail ? problem(503, { traceId: "trace-tx" }) : undefined,
      ),
      signedIn(),
      ...fakeTransactionsApi([tx]).handlers,
      ...fakeDisputesApi().handlers,
    );
    renderApp(`/transactions/${tx.id}/dispute`);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("We could not load this transaction");
    expect(alert).toHaveTextContent("Reference: trace-tx");

    fail = false;
    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));
    expect(
      await screen.findByRole("form", { name: "Dispute this transaction" }),
    ).toBeInTheDocument();
  });
});

describe("dispute form page: reasons", () => {
  it("shows the transaction and a retry when the reasons cannot be loaded, then the form once they can", async () => {
    const tx = makeTransaction({ merchantName: "Netflix" });
    let fail = true;
    server.use(
      http.get("/api/v1/dispute-reasons", () =>
        fail ? problem(503, { traceId: "trace-reasons" }) : undefined,
      ),
      signedIn(),
      ...fakeTransactionsApi([tx]).handlers,
      ...fakeDisputesApi({ transactions: [tx] }).handlers,
    );
    renderApp(`/transactions/${tx.id}/dispute`);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("We could not load the dispute reasons");
    expect(alert).toHaveTextContent("Reference: trace-reasons");
    expect(
      screen.getByRole("heading", { level: 1, name: "Dispute a transaction" }),
    ).toBeInTheDocument();
    expect(screen.queryByRole("form")).not.toBeInTheDocument();

    fail = false;
    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));

    expect(
      await screen.findByRole("form", { name: "Dispute this transaction" }),
    ).toBeInTheDocument();
    expect(within(reasonSelect()).getAllByRole("option")).toHaveLength(4);
  });

  it("shows a loading state, not an empty select, while the reasons arrive", async () => {
    const tx = makeTransaction({ merchantName: "Netflix" });
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    server.use(
      http.get("/api/v1/dispute-reasons", async () => (await gate, undefined)),
      signedIn(),
      ...fakeTransactionsApi([tx]).handlers,
      ...fakeDisputesApi({ transactions: [tx] }).handlers,
    );
    renderApp(`/transactions/${tx.id}/dispute`);

    expect(await screen.findByText("Loading the form...")).toBeInTheDocument();
    expect(screen.queryByRole("form")).not.toBeInTheDocument();

    release();
    expect(
      await screen.findByRole("form", { name: "Dispute this transaction" }),
    ).toBeInTheDocument();
  });
});

describe("dispute form page: validation", () => {
  it("sends nothing while invalid, shows what is wrong, and focuses the first invalid field", async () => {
    const { api, user } = await openForm();

    await user.click(submitButton());

    // (the <option> has the same words, so look for the message paragraph)
    expect(await screen.findByText("Choose a reason", { selector: "p" })).toBeInTheDocument();
    expect(
      screen.getByText("Describe what went wrong, in at least 10 characters"),
    ).toBeInTheDocument();
    expect(reasonSelect()).toHaveFocus();
    expect(api.createBodies).toHaveLength(0);

    for (const field of [reasonSelect(), descriptionBox()]) {
      expect(field).toHaveAttribute("aria-invalid", "true");
      expect(errorText(field)).not.toBe("");
    }
    expect(amountBox()).not.toHaveAttribute("aria-invalid");
  });

  it("focuses the description when only it is invalid", async () => {
    const { api, user } = await openForm();
    await user.selectOptions(reasonSelect(), "duplicate");

    await user.click(submitButton());

    await waitFor(() => expect(descriptionBox()).toHaveFocus());
    expect(api.createBodies).toHaveLength(0);
  });

  it("validates a field on blur, then on every change", async () => {
    const { user } = await openForm();
    await user.click(descriptionBox());
    expect(screen.queryByText(/at least 10 characters/)).not.toBeInTheDocument(); // not before blur

    await user.tab();
    expect(await screen.findByText(/at least 10 characters/)).toBeInTheDocument();

    await user.click(descriptionBox());
    await user.paste("0123456789");
    await waitFor(() =>
      expect(screen.queryByText(/at least 10 characters/)).not.toBeInTheDocument(),
    );
    expect(descriptionBox()).not.toHaveAttribute("aria-invalid");
    expect(errorText(descriptionBox())).toBe("");

    await user.clear(descriptionBox());
    expect(await screen.findByText(/at least 10 characters/)).toBeInTheDocument(); // and back, live
  });

  it("counts the length on the trimmed text: padding around exactly 2000 characters is fine", async () => {
    const { api, user } = await openForm();
    await fillValid(user, { description: `   ${"x".repeat(2000)}   ` });

    await user.click(submitButton());

    await waitFor(() => expect(api.createBodies).toHaveLength(1));
    expect(sent(api).description).toBe("x".repeat(2000));
  });

  it("refuses 2001 characters before any request", async () => {
    const { api, user } = await openForm();
    await fillValid(user, { description: "x".repeat(2001) });

    await user.click(submitButton());

    expect(
      await screen.findByText("Keep the description to 2000 characters or fewer"),
    ).toBeInTheDocument();
    expect(api.createBodies).toHaveLength(0);
    expect(screen.getByText("2001 / 2000")).toHaveClass("text-destructive");
  });

  const MESSAGE_FOR_CODE: Record<string, string> = {
    sensitive_data: SENSITIVE_MESSAGE,
    invalid_characters: INVALID_CHARACTERS_MESSAGE,
  };

  it.each(cases.map((c) => [c.name, c.input, c.code] as const))(
    "description, shared free-text case: %s",
    async (_name, input, code) => {
      const { api, user } = await openForm();
      await user.selectOptions(reasonSelect(), "duplicate");
      // Padding keeps the length rule out of the way; it has no digits, so it cannot join a chain.
      await user.click(descriptionBox()); // so that tabbing away is a blur
      fireEvent.change(descriptionBox(), { target: { value: `Problem with my order. ${input}` } });
      await user.tab();

      if (code === null) {
        await user.click(submitButton());
        await waitFor(() => expect(api.createBodies).toHaveLength(1));
        for (const message of Object.values(MESSAGE_FOR_CODE)) {
          expect(screen.queryByText(message)).not.toBeInTheDocument();
        }
      } else {
        const message = MESSAGE_FOR_CODE[code] ?? "";
        expect(await screen.findByText(message)).toBeInTheDocument();
        expect(errorText(descriptionBox())).toBe(message);
        expect(descriptionBox()).toHaveAttribute("aria-invalid", "true");
        await user.click(submitButton());
        expect(api.createBodies).toHaveLength(0);
        expect(descriptionBox()).toHaveFocus();
      }
    },
  );

  it.each([
    ["abc", "Enter an amount like 49.99, with at most 2 decimals"],
    ["12.345", "Enter an amount like 49.99, with at most 2 decimals"],
    ["-5", "Enter an amount like 49.99, with at most 2 decimals"],
    ["1e2", "Enter an amount like 49.99, with at most 2 decimals"],
    ["0", "The amount must be more than zero"],
    ["0.00", "The amount must be more than zero"],
    ["123.46", /^The amount cannot be more than R\s123,45$/],
    ["1000", /^The amount cannot be more than R\s123,45$/],
  ])("refuses the amount %j with a specific message and sends nothing", async (amount, message) => {
    const { api, user } = await openForm();
    await fillValid(user, { amount });

    await user.click(submitButton());

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(amountBox()).toHaveAttribute("aria-invalid", "true");
    expect(amountBox()).toHaveFocus();
    expect(api.createBodies).toHaveLength(0);
  });

  it.each([
    ["", undefined],
    ["49.99", "49.99"],
    ["50", "50.00"],
    ["50,5", "50.50"],
    ["123.45", "123.45"], // exactly the transaction: allowed
    ["  7.10 ", "7.10"],
  ])("sends the amount %j as %j (omitted when empty)", async (amount, expected) => {
    const { api, user } = await openForm();
    await fillValid(user, { amount });

    await user.click(submitButton());

    await waitFor(() => expect(api.createBodies).toHaveLength(1));
    const body = sent(api);
    if (expected === undefined) expect("amount" in body).toBe(false);
    else expect(body.amount).toBe(expected);
  });

  it("compares the amount to the transaction as a decimal, whatever scale the API sent", async () => {
    const { api, user } = await openForm({ amount: "0.3000" });
    await fillValid(user, { amount: "0.31" });

    await user.click(submitButton());

    expect(await screen.findByText(/^The amount cannot be more than/)).toBeInTheDocument();
    expect(api.createBodies).toHaveLength(0);

    await write(user, amountBox(), ""); // keep focus handling simple: replace the value
    await user.clear(amountBox());
    await user.type(amountBox(), "0.3");
    await user.click(submitButton());
    await waitFor(() => expect(api.createBodies).toHaveLength(1));
    expect(sent(api).amount).toBe("0.30");
  });
});

describe("dispute form page: character counter", () => {
  const live = () => screen.getByRole("status", { name: "Description length" });

  it("counts visibly, announces only at thresholds, and never narrates typing", async () => {
    const { user } = await openForm();
    expect(screen.getByText("0 / 2000")).toBeInTheDocument();
    expect(live()).toBeEmptyDOMElement();
    expect(live()).toHaveAttribute("aria-live", "polite");

    await write(user, descriptionBox(), "x".repeat(100));
    expect(screen.getByText("100 / 2000")).toBeInTheDocument();
    expect(live()).toBeEmptyDOMElement(); // 1900 left: silent

    await user.paste("x".repeat(1700)); // 1800: 200 left
    expect(live()).toHaveTextContent("200 or fewer characters left");
    const node = live().firstChild;
    await user.paste("x".repeat(20)); // still inside the same band
    expect(screen.getByText("1820 / 2000")).toBeInTheDocument();
    expect(live().firstChild).toBe(node); // the text node was not replaced: nothing re-announced

    await user.paste("x".repeat(100)); // 1920: 80 left
    expect(live()).toHaveTextContent("100 or fewer characters left");
    await user.paste("x".repeat(80)); // 2000
    expect(live()).toHaveTextContent("No characters left");
    await user.paste("x");
    expect(live()).toHaveTextContent("Over the limit");
    expect(screen.getByText("2001 / 2000")).toHaveClass("text-destructive");
  });

  it("counts what would be sent: spaces around the text do not count", async () => {
    const { user } = await openForm();
    await write(user, descriptionBox(), "   hello   ");
    expect(screen.getByText("5 / 2000")).toBeInTheDocument();
  });
});

describe("dispute form page: submitting", () => {
  it("sends the trimmed text with a client-made id, then opens the new dispute", async () => {
    const { api, tx, router, user } = await openForm();
    await fillValid(user, { description: `  ${GOOD_TEXT}  ` });

    await user.click(submitButton());

    expect(
      await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" }),
    ).toBeInTheDocument();
    const body = sent(api);
    expect(body).toEqual({
      id: expect.stringMatching(UUID) as string,
      transactionId: tx.id,
      reasonCode: "duplicate",
      description: GOOD_TEXT,
    });
    expect(router.state.location.pathname).toBe(`/disputes/${body.id}`);
    expect(await screen.findByText(/Your dispute was submitted/)).toBeInTheDocument();
    expect(api.store).toHaveLength(1);
  });

  it("shows the created dispute from the response while the page revalidates it", async () => {
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    const { user } = await openForm({}, { interceptGet: async () => (await gate, undefined) });
    await fillValid(user);

    await user.click(submitButton());

    // The detail GET is held open: what is on screen can only have come from the POST response.
    expect(
      await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" }),
    ).toBeInTheDocument();
    release();
  });

  it("marks the lists stale so the transactions and history show the new dispute", async () => {
    const tx = makeTransaction({ merchantName: "Netflix" });
    const transactions = fakeTransactionsApi([tx]);
    const disputes = fakeDisputesApi({ transactions: [tx] });
    server.use(signedIn(), ...transactions.handlers, ...disputes.handlers);
    const { queryClient } = renderApp("/disputes");
    const user = userEvent.setup();
    await screen.findByText("You have not raised any disputes yet");
    await user.click(screen.getByRole("link", { name: "Transactions" }));
    await user.click(await screen.findByRole("link", { name: /^Dispute Netflix/ }));
    await screen.findByRole("form", { name: "Dispute this transaction" });
    await fillValid(user);

    await user.click(submitButton());
    await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" });

    const stale = (key: readonly string[]) => {
      const queries = queryClient.getQueryCache().findAll({ queryKey: key });
      return { count: queries.length, allStale: queries.every((q) => q.state.isInvalidated) };
    };
    expect(stale(["transactions"])).toEqual({
      count: expect.any(Number) as number,
      allStale: true,
    });
    expect(stale(["transactions"]).count).toBeGreaterThan(0);
    expect(stale(["disputes", "list"]).count).toBeGreaterThan(0);
    expect(stale(["disputes", "list"]).allStale).toBe(true);

    // And going back shows it.
    await user.click(screen.getByRole("link", { name: "Transactions" }));
    expect(await screen.findByText("Dispute: Submitted")).toBeInTheDocument();
  });

  it("is a single request however many times it is activated while pending, and says it is working", async () => {
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    const { api, user } = await openForm(
      {},
      { interceptCreate: async () => (await gate, undefined) },
    );
    await fillValid(user);

    await user.click(submitButton());
    const button = submitButton();
    expect(button).toHaveAccessibleName("Submitting...");
    expect(button).toHaveAttribute("aria-disabled", "true");
    expect(screen.getByRole("status", { name: "Submission status" })).toHaveTextContent(
      "Submitting your dispute...",
    );
    await user.click(button);
    await user.type(descriptionBox(), "{Enter}"); // Enter in a field submits the form natively
    await user.keyboard("{Enter}");

    release();
    await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" });
    expect(api.createBodies).toHaveLength(1);
  });

  it("sends one request even when the form is submitted twice in the same instant", async () => {
    const { api, user } = await openForm();
    await fillValid(user);

    const form = screen.getByRole("form", { name: "Dispute this transaction" });
    fireEvent.submit(form);
    fireEvent.submit(form); // before React has had any chance to show "pending"

    await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" });
    expect(api.createBodies).toHaveLength(1);
    expect(user).toBeDefined();
  });

  it("keeps focus on the submit button while pending (aria-disabled, not disabled)", async () => {
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    const { user } = await openForm({}, { interceptCreate: async () => (await gate, undefined) });
    await fillValid(user);

    await user.click(submitButton());

    expect(submitButton()).toHaveFocus();
    release();
    await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" });
  });

  it("Cancel goes back to the transactions", async () => {
    const { router, user } = await openForm();
    await user.click(screen.getByRole("link", { name: "Cancel" }));
    expect(router.state.location.pathname).toBe("/transactions");
  });
});

describe("dispute form page: one id per opened form", () => {
  it("a lost response then a retry sends the SAME id, and the server's 200 replay is a success", async () => {
    const { api, user } = await openForm(
      {},
      {
        // The server processes the first attempt, but the answer never arrives.
        afterCreate: (() => {
          let first = true;
          return () => {
            if (!first) return undefined;
            first = false;
            return HttpResponse.error();
          };
        })(),
      },
    );
    await fillValid(user);

    await user.click(submitButton());
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("We could not submit your dispute");
    expect(alert).toHaveTextContent("Trying again will not create a second dispute");
    expect(api.store).toHaveLength(1); // it really was created

    await user.click(screen.getByRole("button", { name: "Try again" }));

    await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" });
    expect(api.createBodies).toHaveLength(2);
    expect(sent(api, 1).id).toBe(sent(api, 0).id);
    expect(api.store).toHaveLength(1); // the replay did not make a second one
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("a 503 then the main Submit button again also reuses the id, even after editing the text", async () => {
    let fail = true;
    const { api, user } = await openForm(
      {},
      { interceptCreate: () => (fail ? problem(503, { traceId: "trace-503" }) : undefined) },
    );
    await fillValid(user);
    await user.click(submitButton());
    expect(await screen.findByRole("alert")).toHaveTextContent("Reference: trace-503");

    fail = false;
    await write(user, descriptionBox(), " And I contacted the shop.");
    await user.click(submitButton());

    await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" });
    expect(sent(api, 1).id).toBe(sent(api, 0).id);
    expect(sent(api, 1).description).toContain("And I contacted the shop.");
  });

  it("opening the form again makes a NEW id", async () => {
    const { api, user } = await openForm({}, { interceptCreate: () => problem(503) });
    await fillValid(user);
    await user.click(submitButton());
    await screen.findByRole("alert");

    await user.click(screen.getByRole("link", { name: "Cancel" }));
    await user.click(await screen.findByRole("link", { name: /^Dispute Netflix/ }));
    await screen.findByRole("form", { name: "Dispute this transaction" });
    await fillValid(user);
    await user.click(submitButton());
    await waitFor(() => expect(api.createBodies).toHaveLength(2));

    expect(sent(api, 1).id).toMatch(UUID);
    expect(sent(api, 1).id).not.toBe(sent(api, 0).id);
  });

  it("keeps ONE id across a failed submit and its retry under React.StrictMode", async () => {
    // StrictMode double-invokes state initialisers and re-runs effects: an id made anywhere but
    // once per mounted form would show up here as two different ids.
    const made = vi.spyOn(crypto, "randomUUID");
    try {
      let fail = true;
      const { api, user } = await openForm(
        {},
        { interceptCreate: () => (fail ? problem(503, { traceId: "trace-strict" }) : undefined) },
        { strict: true },
      );
      expect(made.mock.calls.length).toBeGreaterThan(1); // StrictMode really did call it twice
      await fillValid(user);

      await user.click(submitButton());
      expect(await screen.findByRole("alert")).toHaveTextContent("Reference: trace-strict");
      fail = false;
      await user.click(submitButton());

      await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" });
      expect(api.createBodies).toHaveLength(2);
      expect(sent(api, 1).id).toBe(sent(api, 0).id);
      expect(sent(api, 0).id).toMatch(UUID);
    } finally {
      made.mockRestore();
    }
  });

  it("makes a different id for each opened form", async () => {
    const ids = new Set<string>();
    for (let i = 0; i < 3; i++) {
      const { api, user, unmount } = await openForm({}, { interceptCreate: () => problem(503) });
      await fillValid(user);
      await user.click(submitButton());
      await screen.findByRole("alert");
      ids.add(sent(api).id);
      unmount();
    }
    expect(ids.size).toBe(3);
  });
});

describe("dispute form page: a replay that kept the earlier details", () => {
  const NOTE = /We had already received your request, so we kept what you sent the first time/;
  const EDITED = "A completely different and corrected description.";

  /** The server stores the first attempt, but its answer is lost on the way back. */
  const loseTheFirstAnswer = () => {
    let first = true;
    return () => {
      if (!first) return undefined;
      first = false;
      return HttpResponse.error();
    };
  };

  const banner = () => screen.getByText(/Your dispute was submitted/).closest('[role="status"]');

  it("tells the customer when the text they edited was not stored, and shows the stored text", async () => {
    const { api, user } = await openForm({}, { afterCreate: loseTheFirstAnswer() });
    await fillValid(user);
    await user.click(submitButton());
    await screen.findByRole("alert"); // network failure: the form keeps what was typed
    await user.clear(descriptionBox());
    await write(user, descriptionBox(), EDITED);

    await user.click(submitButton());

    await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" });
    expect(sent(api, 1).id).toBe(sent(api, 0).id);
    expect(sent(api, 1).description).toBe(EDITED);
    expect(api.store).toHaveLength(1);
    expect(banner()).toHaveTextContent(NOTE);
    expect(banner()).toHaveTextContent("withdraw this dispute and raise a new one");
    expect(banner()).toHaveFocus(); // one live region, announced whole
    expect(screen.getByText(GOOD_TEXT)).toBeInTheDocument(); // the first text is what is stored
    expect(screen.queryByText(EDITED)).not.toBeInTheDocument();
  });

  it("keeps the plain banner when the replay stored exactly what was sent", async () => {
    const { user } = await openForm({}, { afterCreate: loseTheFirstAnswer() });
    await fillValid(user);
    await user.click(submitButton());
    await screen.findByRole("alert");

    await user.click(submitButton()); // nothing edited

    await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" });
    expect(banner()).toHaveTextContent("Your dispute was submitted.");
    expect(screen.queryByText(NOTE)).not.toBeInTheDocument();
  });

  it("ignores whitespace around the text when comparing", async () => {
    const { user } = await openForm({}, { afterCreate: loseTheFirstAnswer() });
    await fillValid(user);
    await user.click(submitButton());
    await screen.findByRole("alert");
    await write(user, descriptionBox(), "   "); // only padding added

    await user.click(submitButton());

    await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" });
    expect(screen.queryByText(NOTE)).not.toBeInTheDocument();
  });

  it.each([
    ["another reason", { reason: "unauthorised" }, { reason: "duplicate" }],
    ["a smaller amount where the whole was first sent", { amount: "10" }, {}],
    ["the whole amount where a smaller one was first sent", {}, { amount: "10" }],
  ])("also says so when the retry changed %s", async (_what, retry, first) => {
    const { user } = await openForm({}, { afterCreate: loseTheFirstAnswer() });
    await fillValid(user, first);
    await user.click(submitButton());
    await screen.findByRole("alert");

    if ("reason" in retry) await user.selectOptions(reasonSelect(), retry.reason);
    if ("amount" in retry) await write(user, amountBox(), retry.amount);
    if (!("amount" in retry) && "amount" in first) await user.clear(amountBox());
    await user.click(submitButton());

    await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" });
    expect(banner()).toHaveTextContent(NOTE);
  });

  it("is never shown for a first-time submit", async () => {
    const { user } = await openForm();
    await fillValid(user);

    await user.click(submitButton());

    await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" });
    expect(banner()).toHaveTextContent("Your dispute was submitted.");
    expect(screen.queryByText(NOTE)).not.toBeInTheDocument();
  });
});

describe("dispute form page: server errors", () => {
  const TRACE = "trace-form-1";
  const submitWith = async (options: FakeOptions, overrides: Partial<TransactionItem> = {}) => {
    const opened = await openForm(overrides, options);
    await fillValid(opened.user);
    await opened.user.click(submitButton());
    return opened;
  };

  it("400 validation_failed: puts each message beside its field, focuses the first, shows the reference", async () => {
    await submitWith({
      interceptCreate: () =>
        problem(400, {
          code: "validation_failed",
          traceId: TRACE,
          errors: { Description: ["The server dislikes this."], amount: ["Bad amount."] },
        }),
    });

    expect(await screen.findByText("The server dislikes this.")).toBeInTheDocument();
    expect(screen.getByText("Bad amount.")).toBeInTheDocument();
    expect(errorText(descriptionBox())).toBe("The server dislikes this.");
    expect(errorText(amountBox())).toBe("Bad amount.");
    expect(descriptionBox()).toHaveAttribute("aria-invalid", "true");
    expect(descriptionBox()).toHaveFocus();
    expect(reasonSelect()).not.toHaveAttribute("aria-invalid");
    expect(screen.getByText(TRACE)).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /could not submit/i })).not.toBeInTheDocument();
  });

  it("a field error from the server goes away as soon as the customer edits that field", async () => {
    const { user } = await submitWith({
      interceptCreate: () =>
        problem(400, { code: "validation_failed", errors: { description: ["Server says no."] } }),
    });
    await screen.findByText("Server says no.");

    await write(user, descriptionBox(), " More detail.");

    await waitFor(() => expect(screen.queryByText("Server says no.")).not.toBeInTheDocument());
    expect(descriptionBox()).not.toHaveAttribute("aria-invalid");
  });

  it("400 about a field the form does not have is listed, not lost", async () => {
    await submitWith({
      interceptCreate: () =>
        problem(400, {
          code: "validation_failed",
          traceId: TRACE,
          errors: { transactionId: ["Bad id."] },
        }),
    });

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Please check your dispute");
    expect(alert).toHaveTextContent("Bad id.");
    expect(alert).toHaveTextContent(`Reference: ${TRACE}`);
  });

  it("400 sensitive_data from the server lands on the description, in our words", async () => {
    await submitWith({
      interceptCreate: () =>
        problem(400, {
          code: "sensitive_data",
          traceId: TRACE,
          errors: { description: ["Remove numbers."] },
        }),
    });

    expect(await screen.findByText(SENSITIVE_MESSAGE)).toBeInTheDocument();
    expect(errorText(descriptionBox())).toBe(SENSITIVE_MESSAGE);
    expect(descriptionBox()).toHaveFocus();
    expect(screen.getByText(TRACE)).toBeInTheDocument();
  });

  it("400 invalid_characters from the server lands on the description, in our words", async () => {
    await submitWith({
      interceptCreate: () =>
        problem(400, {
          code: "invalid_characters",
          traceId: TRACE,
          errors: { description: ["Remove control characters."] },
        }),
    });

    expect(await screen.findByText(INVALID_CHARACTERS_MESSAGE)).toBeInTheDocument();
    expect(errorText(descriptionBox())).toBe(INVALID_CHARACTERS_MESSAGE);
    expect(descriptionBox()).toHaveFocus();
    expect(screen.getByText(TRACE)).toBeInTheDocument();
  });

  it.each([
    ["not_posted", "Pending transactions can't be disputed yet"],
    ["not_debit", "Only debits can be disputed"],
    ["window_expired", "The 120-day dispute window has closed"],
    ["amount_exceeds_transaction", "The amount is more than the transaction"],
    ["amount_not_positive", "The amount must be more than zero"],
    [
      "too_many_active_disputes",
      "You have too many open disputes. Wait for some to be decided, or withdraw one",
    ],
    [
      "too_many_disputes_for_transaction",
      "This transaction has reached the most disputes it can have",
    ],
  ])("422 %s is explained in plain language", async (code, sentence) => {
    await submitWith({
      interceptCreate: () =>
        problem(422, {
          code: "eligibility_failed",
          traceId: TRACE,
          violations: [{ code, message: `server: ${code}` }],
        }),
    });

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("This dispute can't be submitted");
    expect(within(alert).getByRole("listitem")).toHaveTextContent(sentence);
    expect(alert).not.toHaveTextContent("server:");
    expect(alert).toHaveTextContent(`Reference: ${TRACE}`);
  });

  it("422 lists every rule that was broken, and the alert takes focus", async () => {
    await submitWith({
      interceptCreate: () =>
        problem(422, {
          code: "eligibility_failed",
          violations: [
            { code: "not_posted", message: "x" },
            { code: "window_expired", message: "y" },
            { code: "future_rule", message: "A rule from the future." },
          ],
        }),
    });

    const alert = await screen.findByRole("alert");
    expect(
      within(alert)
        .getAllByRole("listitem")
        .map((li) => li.textContent),
    ).toEqual([
      "Pending transactions can't be disputed yet",
      "The 120-day dispute window has closed",
      "A rule from the future.",
    ]);
    expect(alert.parentElement).toHaveFocus();
  });

  it("409 dispute_exists links to the existing dispute, and the link works", async () => {
    const existing = makeDisputeDetail({ merchantName: "Netflix" });
    const { router, user } = await submitWith({
      disputes: [existing],
      interceptCreate: () =>
        problem(409, { code: "dispute_exists", traceId: TRACE, existingDisputeId: existing.id }),
    });

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("This transaction already has a dispute");
    expect(alert).toHaveTextContent(`Reference: ${TRACE}`);
    const link = within(alert).getByRole("link", { name: "View the existing dispute" });
    expect(link).toHaveAttribute("href", `/disputes/${existing.id}`);

    await user.click(link);

    expect(
      await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" }),
    ).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/disputes/${existing.id}`);
  });

  it("409 dispute_exists without a usable id falls back to the history", async () => {
    await submitWith({
      interceptCreate: () => problem(409, { code: "dispute_exists", existingDisputeId: "../x" }),
    });
    const alert = await screen.findByRole("alert");
    expect(within(alert).getByRole("link", { name: "View my disputes" })).toHaveAttribute(
      "href",
      "/disputes",
    );
  });

  it.each(["idempotency_key_reuse", "dispute_id_conflict"])(
    "409 %s says an earlier attempt may exist, points at the history, and does not invent a new id",
    async (code) => {
      const { api, user } = await submitWith({
        interceptCreate: () => problem(409, { code, traceId: TRACE }),
      });

      const alert = await screen.findByRole("alert");
      expect(alert).toHaveTextContent("We may already have this request");
      expect(alert).toHaveTextContent(`Reference: ${TRACE}`);
      expect(within(alert).getByRole("link", { name: "View my disputes" })).toHaveAttribute(
        "href",
        "/disputes",
      );
      expect(within(alert).queryByRole("button")).not.toBeInTheDocument();

      await user.click(submitButton());
      await waitFor(() => expect(api.createBodies).toHaveLength(2));
      expect(sent(api, 1).id).toBe(sent(api, 0).id);
    },
  );

  it("404 says the transaction was not found, with a way back", async () => {
    await submitWith({
      interceptCreate: () => problem(404, { code: "not_found", traceId: TRACE }),
    });

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Transaction not found");
    expect(alert).toHaveTextContent(`Reference: ${TRACE}`);
    expect(within(alert).getByRole("link", { name: "Back to my transactions" })).toHaveAttribute(
      "href",
      "/transactions",
    );
  });

  it.each([
    ["30", "Too many attempts, try again in 30 seconds."],
    ["1", "Too many attempts, try again in 1 second."],
    [undefined, "Too many attempts, try again in a moment."],
    ["Wed, 21 Oct 2026 07:28:00 GMT", "Too many attempts, try again in a moment."],
  ])("429 with Retry-After %j says %j", async (retryAfter, sentence) => {
    await submitWith({
      interceptCreate: () =>
        problem(
          429,
          { code: "rate_limited", traceId: TRACE },
          retryAfter ? { "Retry-After": retryAfter } : {},
        ),
    });

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(sentence);
    expect(alert).toHaveTextContent(`Reference: ${TRACE}`);
  });

  it("5xx offers a retry that works and keeps what was typed", async () => {
    let fail = true;
    const { api, user } = await submitWith({
      interceptCreate: () => (fail ? problem(500, { traceId: TRACE }) : undefined),
    });

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("We could not submit your dispute");
    expect(alert).toHaveTextContent(`Reference: ${TRACE}`);
    expect(descriptionBox()).toHaveValue(GOOD_TEXT);
    expect(reasonSelect()).toHaveValue("duplicate");

    fail = false;
    await user.click(within(alert).getByRole("button", { name: "Try again" }));

    await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" });
    expect(api.createBodies).toHaveLength(2);
  });

  it("a network failure offers a retry in plain words, with no reference to quote", async () => {
    await submitWith({ interceptCreate: () => HttpResponse.error() });

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("We could not submit your dispute");
    expect(alert).toHaveTextContent("We could not reach the server.");
    expect(alert).not.toHaveTextContent("Reference");
    expect(within(alert).getByRole("button", { name: "Try again" })).toBeInTheDocument();
  });

  it("clears the previous failure when the customer submits again", async () => {
    let fail = true;
    const { api, user } = await submitWith({
      interceptCreate: () => (fail ? problem(500, { traceId: TRACE }) : undefined),
    });
    await screen.findByRole("alert");

    fail = false;
    await user.click(submitButton());

    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
    await screen.findByRole("heading", { level: 1, name: "Dispute: Netflix" });
    expect(api.createBodies).toHaveLength(2);
  });
});
