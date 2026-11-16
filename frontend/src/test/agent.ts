import { http, HttpResponse } from "msw";

import type { AgentCustomerTransaction, AgentDisputeDetail, TransitionRequest } from "@/api/agent";
import type { DisputeStatus } from "@/api/disputes";
import { freeTextProblem } from "@/lib/validation";

import { problem } from "./handlers";
import { makeDisputeDetail, reasons } from "./disputes";

const AGENT = "aaaaaaaa-0000-4000-8000-000000000001";
/** The colleague behind `otherAgentActs`. */
const OTHER_AGENT = "bbbbbbbb-0000-4000-8000-000000000002";
const OPEN: DisputeStatus[] = ["Submitted", "UnderReview"];
const NEXT: Partial<Record<DisputeStatus, DisputeStatus[]>> = {
  Submitted: ["UnderReview"],
  UnderReview: ["Accepted", "Rejected"],
};

type Assignment = Pick<AgentDisputeDetail, "assignment" | "assigneeId" | "claimedAt">;

/**
 * Who is reviewing, as the server reports it for a status (ADR-019): an UnderReview dispute is the
 * assignee's (the fake agent unless a colleague acted), claimed at `at`; a decided or withdrawn
 * one is nobody's. The fake does not refuse a non-assignee's move (409 not_assignee) yet.
 */
function assignmentFor(status: DisputeStatus, at: string, assignee = AGENT): Assignment {
  if (status === "Submitted") return { assignment: "Unclaimed", assigneeId: null, claimedAt: null };
  if (status === "UnderReview") {
    return {
      assignment: assignee === AGENT ? "You" : "AnotherAgent",
      assigneeId: assignee,
      claimedAt: at,
    };
  }
  return { assignment: null, assigneeId: null, claimedAt: null };
}

/** An agent's view of a dispute; `index` 0 is the OLDEST. */
export function makeAgentDispute(
  overrides: Partial<AgentDisputeDetail> = {},
  index = 0,
): AgentDisputeDetail {
  const base = makeDisputeDetail({}, index);
  const { timeline, ...rest } = base;
  // The customer API's dates run backwards with the index; the queue is oldest first.
  const createdAt = new Date(Date.UTC(2026, 9, 1 + index, 9)).toISOString();
  return {
    ...rest,
    createdAt,
    updatedAt: createdAt,
    customerDisplayName: `Customer ${index + 1}`,
    maskedAccountNumber: `****${String(1000 + index)}`,
    timeline: timeline.map((entry) => ({
      ...entry,
      actorId: "cccccccc-0000-4000-8000-000000000001",
      occurredAt: createdAt,
    })),
    ...assignmentFor(overrides.status ?? rest.status, createdAt),
    ...overrides,
  };
}

/** The first day of the window the fake reports: a date, as the API sends it. */
export const CUSTOMER_TRANSACTIONS_FROM = "2026-08-04";

/** A posted debit of the customer behind a dispute; `index` 0 is the NEWEST. Its account differs from the detail's. */
export function makeAgentCustomerTransaction(
  overrides: Partial<AgentCustomerTransaction> = {},
  index = 0,
): AgentCustomerTransaction {
  const n = String(index + 1).padStart(4, "0");
  const date = Date.UTC(2026, 10, 3 - index, 10);
  return {
    id: `eeeeeeee-0000-4000-8000-${String(index + 1).padStart(12, "0")}`,
    reference: `TXN-${n}`,
    merchantName: `Shop ${n}`,
    category: "Groceries",
    amount: "123.45",
    currency: "ZAR",
    direction: "Debit",
    status: "Posted",
    transactionDate: new Date(date).toISOString(),
    postedAt: new Date(date + 3_600_000).toISOString(),
    maskedAccountNumber: "**** 7788",
    dispute: null,
    ...overrides,
  };
}

export interface FakeAgentApi {
  readonly store: AgentDisputeDetail[];
  readonly queueRequests: URL[];
  /** Every customer-transactions request, with its path's dispute id. */
  readonly customerTransactionRequests: { id: string; url: URL }[];
  readonly transitionCalls: { id: string; ifMatch: string | null; body: TransitionRequest }[];
  /** Another agent acts: the ETag moves on. */
  otherAgentActs(id: string, to: DisputeStatus): void;
  /** The version moves on but the status does not: a refetch brings a newer ETag, same page. */
  versionMovesOn(id: string): void;
}

const etagOf = (version: number) => `"v${version}"`;

/**
 * The agent endpoints with the server's rules: 428 / 412 / 409 / 400, ETag on every read. The
 * customer's transactions (newest first, any dispute of the store reads the same list) default to none.
 */
export function fakeAgentApi(
  initial: AgentDisputeDetail[],
  customerTransactions: AgentCustomerTransaction[] = [],
) {
  const store = [...initial];
  const versions = new Map(store.map((d) => [d.id, 1]));
  const api: FakeAgentApi = {
    store,
    queueRequests: [],
    customerTransactionRequests: [],
    transitionCalls: [],
    otherAgentActs(id, to) {
      apply(id, to, "Handled elsewhere", OTHER_AGENT);
    },
    versionMovesOn(id) {
      versions.set(id, (versions.get(id) ?? 1) + 1);
    },
  };

  function apply(id: string, to: DisputeStatus, note: string | null, actor = AGENT) {
    const index = store.findIndex((d) => d.id === id);
    const current = store[index];
    if (!current) throw new Error(`no dispute ${id}`);
    const at = new Date(Date.UTC(2026, 10, 4, 10, versions.get(id) ?? 1)).toISOString();
    const done = !OPEN.includes(to);
    store[index] = {
      ...current,
      status: to,
      updatedAt: at,
      resolvedAt: done ? at : null,
      resolutionNote: done ? note : null,
      ...assignmentFor(to, at, actor),
      timeline: [
        ...current.timeline,
        { from: current.status, to, actorType: "Agent", actorId: actor, note, occurredAt: at },
      ],
    };
    versions.set(id, (versions.get(id) ?? 1) + 1);
  }
  const headers = (id: string) => ({ ETag: etagOf(versions.get(id) ?? 1) });

  const queue = http.get("/api/v1/agent/disputes", ({ request }) => {
    const url = new URL(request.url);
    api.queueRequests.push(url);
    const limit = Number(url.searchParams.get("limit") ?? 25);
    const status = url.searchParams.get("status");
    const offsetText = url.searchParams.get("cursor");
    const offset = offsetText ? Number(atob(offsetText).replace("cursor:", "")) : 0;
    const matching = store
      .filter((d) => (status ? d.status === status : OPEN.includes(d.status)))
      .sort((a, b) => a.createdAt.localeCompare(b.createdAt));
    const items = matching.slice(offset, offset + limit).map((d) => ({
      id: d.id,
      status: d.status,
      createdAt: d.createdAt,
      merchantName: d.transaction.merchantName,
      amount: d.amount,
      currency: d.currency,
      reasonCode: d.reasonCode,
      customerDisplayName: d.customerDisplayName,
      maskedAccountNumber: d.maskedAccountNumber,
      assignment: d.assignment,
      assigneeId: d.assigneeId,
      claimedAt: d.claimedAt,
    }));
    const next = offset + limit;
    return HttpResponse.json({
      items,
      nextCursor: next < matching.length ? btoa(`cursor:${next}`) : null,
    });
  });

  const get = http.get("/api/v1/agent/disputes/:id", ({ params }) => {
    const id = String(params["id"]);
    const found = store.find((d) => d.id === id);
    return found
      ? HttpResponse.json(found, { headers: headers(id) })
      : problem(404, { title: "Not Found", code: "not_found" });
  });

  // The server reads a row's dispute from the database, so a decision shows on the next read.
  function withLiveDispute(item: AgentCustomerTransaction): AgentCustomerTransaction {
    const live = store.find((d) => d.id === item.dispute?.id);
    return live ? { ...item, dispute: { id: live.id, status: live.status } } : item;
  }

  const customerList = http.get(
    "/api/v1/agent/disputes/:id/customer-transactions",
    ({ params, request }) => {
      const id = String(params["id"]);
      const url = new URL(request.url);
      api.customerTransactionRequests.push({ id, url });
      // The server's rule: only a dispute still under review opens its customer's transactions.
      const dispute = store.find((d) => d.id === id);
      if (!dispute || !OPEN.includes(dispute.status)) {
        return problem(404, { title: "Not Found", code: "dispute_not_found" });
      }
      const limit = Number(url.searchParams.get("limit") ?? 25);
      const cursor = url.searchParams.get("cursor");
      const offset = cursor ? Number(atob(cursor).replace("cursor:", "")) : 0;
      const next = offset + limit;
      return HttpResponse.json({
        items: customerTransactions.slice(offset, next).map(withLiveDispute),
        nextCursor: next < customerTransactions.length ? btoa(`cursor:${next}`) : null,
        from: CUSTOMER_TRANSACTIONS_FROM,
      });
    },
  );

  const transition = http.post(
    "/api/v1/agent/disputes/:id/transitions",
    async ({ params, request }) => {
      const id = String(params["id"]);
      const ifMatch = request.headers.get("if-match");
      const body = (await request.json()) as TransitionRequest;
      api.transitionCalls.push({ id, ifMatch, body });
      const current = store.find((d) => d.id === id);
      if (!current) return problem(404, { title: "Not Found", code: "not_found" });
      if (ifMatch === null) {
        return problem(428, { title: "Precondition Required", code: "precondition_required" });
      }
      if (ifMatch !== etagOf(versions.get(id) ?? 1)) {
        return problem(
          412,
          { title: "Precondition Failed", code: "precondition_failed" },
          headers(id),
        );
      }
      const note = body.note?.trim() ?? "";
      if (body.toStatus !== "UnderReview" && note === "") {
        return problem(400, {
          title: "One or more validation errors occurred.",
          code: "validation_failed",
          errors: { note: ["note is required to accept or reject a dispute."] },
        });
      }
      const rule = freeTextProblem(body.note ?? "");
      if (rule !== null) {
        return problem(400, {
          title: "Bad note",
          code: rule,
          errors: { note: [`note failed: ${rule}`] },
        });
      }
      if (!NEXT[current.status]?.includes(body.toStatus)) {
        return problem(409, {
          title: "Conflict",
          detail: `A ${current.status} dispute cannot move to ${body.toStatus}.`,
          code: "invalid_transition",
        });
      }
      apply(id, body.toStatus, note === "" ? null : note);
      return HttpResponse.json(
        store.find((d) => d.id === id),
        { headers: headers(id) },
      );
    },
  );

  const reasonList = http.get("/api/v1/dispute-reasons", () => HttpResponse.json(reasons));
  return { api, handlers: [reasonList, queue, get, customerList, transition] };
}
