import { http, HttpResponse } from "msw";

import type {
  CreateDisputeRequest,
  DisputeDetail,
  DisputeReason,
  DisputeStatus,
} from "@/api/disputes";
import type { TransactionItem } from "@/api/transactions";
import { freeTextProblem } from "@/lib/validation";

import { problem } from "./handlers";

export const reasons: DisputeReason[] = [
  {
    code: "unauthorised",
    label: "I did not make this purchase",
    description: "You do not recognise the charge and did not authorise it.",
  },
  {
    code: "duplicate",
    label: "I was charged twice",
    description: "The same purchase appears more than once.",
  },
  {
    code: "not_received",
    label: "I never received the goods or service",
    description: "You paid but the order never arrived.",
  },
];

const DAY_MS = 86_400_000;
const NOW = Date.UTC(2026, 10, 4, 9, 0, 0);

let counter = 0;

/** A Submitted dispute with a one-entry timeline unless overridden. */
export function makeDisputeDetail(
  overrides: Partial<DisputeDetail> = {},
  index = 0,
): DisputeDetail {
  const n = String(index + 1).padStart(4, "0");
  const id = overrides.id ?? `dddddddd-0000-4000-8000-${String(index + 1).padStart(12, "0")}`;
  const createdAt = new Date(NOW - index * DAY_MS).toISOString();
  const merchantName = overrides.merchantName ?? `Merchant ${n}`;
  const amount = overrides.amount ?? "123.45";
  const transactionId =
    overrides.transactionId ?? `00000000-0000-4000-8000-${String(index + 1).padStart(12, "0")}`;
  return {
    id,
    transactionId,
    merchantName,
    amount,
    currency: "ZAR",
    reasonCode: "unauthorised",
    status: "Submitted",
    createdAt,
    updatedAt: createdAt,
    resolvedAt: null,
    description: "I did not make this purchase at all.",
    resolutionNote: null,
    timeline: [
      { from: null, to: "Submitted", actorType: "Customer", note: null, occurredAt: createdAt },
    ],
    transaction: {
      id: transactionId,
      reference: `REF-${n}`,
      merchantName,
      amount,
      currency: "ZAR",
      transactionDate: new Date(NOW - (index + 5) * DAY_MS).toISOString(),
    },
    ...overrides,
  };
}

export interface FakeDisputes {
  /** Every dispute, newest first. */
  readonly store: DisputeDetail[];
  /** Each POST /disputes body the server received, in order. */
  readonly createBodies: CreateDisputeRequest[];
  /** Every withdraw call: the dispute id and the If-Match header it carried (null if absent). */
  readonly withdrawCalls: { id: string; ifMatch: string | null }[];
  readonly requests: URL[];
  /** An agent acts on a dispute behind the customer's back: bumps the ETag. */
  agentActs(id: string, to: DisputeStatus, note?: string): void;
}

interface FakeDisputesOptions {
  /** Starting disputes, newest first. */
  disputes?: DisputeDetail[];
  /** The customer's transactions: used for 404 and eligibility, and updated on create. */
  transactions?: TransactionItem[];
  /** Short-circuit POST /disputes (e.g. a 429, a 5xx or a network error). undefined falls through. */
  interceptCreate?: (
    body: CreateDisputeRequest,
  ) => Response | undefined | Promise<Response | undefined>;
  /** Short-circuit GET /disputes/{id}. */
  interceptGet?: (id: string) => Response | undefined | Promise<Response | undefined>;
  /**
   * Replace the answer to a create that WAS processed (the dispute is stored), e.g. with a
   * network error: the lost-response case an idempotent retry exists for.
   */
  afterCreate?: (created: DisputeDetail) => Response | undefined | Promise<Response | undefined>;
  /** Short-circuit POST /disputes/{id}/withdraw, after the call is recorded in `withdrawCalls`. */
  interceptWithdraw?: (id: string) => Response | undefined | Promise<Response | undefined>;
}

const etagOf = (version: number) => `"v${version}"`;
const TERMINAL: DisputeStatus[] = ["Accepted", "Rejected", "Withdrawn"];
const ACTIVE: DisputeStatus[] = ["Submitted", "UnderReview"];

/** Decimal comparison without floats, for the amount rules. */
function cents(value: string): number {
  return Math.round(Number(value) * 100);
}

/**
 * Stands in for the whole disputes API, with the server's rules and its status codes
 * (ADR-007): validation 400 before anything else, 404 for another customer's transaction,
 * 422 listing every violated rule, 409 for an active dispute, idempotent replay by id, ETag
 * on every read, and 428/412/409 on withdraw.
 */
export function fakeDisputesApi(options: FakeDisputesOptions = {}) {
  const store: DisputeDetail[] = [...(options.disputes ?? [])];
  const versions = new Map<string, number>(store.map((d) => [d.id, 1]));
  const transactions = options.transactions ?? [];
  const api: FakeDisputes = {
    store,
    createBodies: [],
    withdrawCalls: [],
    requests: [],
    agentActs(id, to, note) {
      const index = store.findIndex((d) => d.id === id);
      const current = store[index];
      if (!current) throw new Error(`no dispute ${id}`);
      const at = new Date(NOW + (counter += 1) * 60_000).toISOString();
      store[index] = {
        ...current,
        status: to,
        updatedAt: at,
        resolvedAt: TERMINAL.includes(to) ? at : null,
        resolutionNote: TERMINAL.includes(to) ? (note ?? null) : current.resolutionNote,
        timeline: [
          ...current.timeline,
          { from: current.status, to, actorType: "Agent", note: note ?? null, occurredAt: at },
        ],
      };
      versions.set(id, (versions.get(id) ?? 1) + 1);
    },
  };
  const detailHeaders = (id: string) => ({ ETag: etagOf(versions.get(id) ?? 1) });

  const reasonsHandler = http.get("/api/v1/dispute-reasons", () => HttpResponse.json(reasons));

  const createHandler = http.post("/api/v1/disputes", async ({ request }) => {
    const body = (await request.json()) as CreateDisputeRequest;
    api.createBodies.push(body);
    const intercepted = await options.interceptCreate?.(body);
    if (intercepted) return intercepted;

    // Idempotent replay (ADR-007): the same id for the same transaction returns the dispute as it
    // is now, whatever the body says (an edited text is NOT stored: the first one is kept); the
    // same id for another transaction is 409 idempotency_key_reuse.
    const existing = store.find((d) => d.id === body.id);
    if (existing) {
      return existing.transactionId === body.transactionId
        ? HttpResponse.json(existing, { status: 200, headers: detailHeaders(existing.id) })
        : problem(409, {
            title: "Conflict",
            code: "idempotency_key_reuse",
            detail: "That id was used for a different transaction.",
          });
    }

    const errors: Record<string, string[]> = {};
    const description = (body.description ?? "").trim();
    if (description.length < 10 || description.length > 2000) {
      errors["description"] = ["Description must be 10 to 2000 characters."];
    }
    if (!reasons.some((r) => r.code === body.reasonCode))
      errors["reasonCode"] = ["Unknown reason."];
    if (body.amount !== undefined && !/^\d+(\.\d{1,2})?$/.test(body.amount)) {
      errors["amount"] = ["Amount must be a decimal with at most 2 places."];
    }
    if (Object.keys(errors).length > 0) {
      return problem(400, {
        title: "One or more validation errors occurred.",
        code: "validation_failed",
        errors,
      });
    }
    // The same two rules, in the same order, as the shared validation-cases.json describes.
    const rule = freeTextProblem(body.description);
    if (rule === "invalid_characters") {
      return problem(400, {
        title: "Invalid characters",
        code: "invalid_characters",
        errors: { description: ["Remove control characters."] },
      });
    }
    if (rule === "sensitive_data") {
      return problem(400, {
        title: "Sensitive data",
        code: "sensitive_data",
        errors: { description: ["Remove card or ID numbers."] },
      });
    }

    const transaction = transactions.find((t) => t.id === body.transactionId);
    if (!transaction) {
      return problem(404, {
        title: "Not Found",
        detail: "Transaction not found.",
        code: "not_found",
      });
    }

    const violations: { code: string; message: string }[] = [];
    for (const reason of transaction.disputeBlockReasons) {
      if (reason !== "already_disputed") violations.push({ code: reason, message: reason });
    }
    if (body.amount !== undefined) {
      if (cents(body.amount) <= 0)
        violations.push({ code: "amount_not_positive", message: "amount" });
      else if (cents(body.amount) > cents(transaction.amount)) {
        violations.push({ code: "amount_exceeds_transaction", message: "amount" });
      }
    }
    if (violations.length > 0) {
      return problem(422, {
        title: "Not eligible",
        code: "eligibility_failed",
        detail: "This transaction cannot be disputed.",
        violations,
      });
    }

    const active = store.find(
      (d) => d.transactionId === body.transactionId && ACTIVE.includes(d.status),
    );
    if (active || transaction.disputeBlockReasons.includes("already_disputed")) {
      return problem(409, {
        title: "Conflict",
        detail: "This transaction already has an active dispute.",
        code: "dispute_exists",
        existingDisputeId: active?.id ?? transaction.dispute?.id,
      });
    }

    const at = new Date(NOW + (counter += 1) * 60_000).toISOString();
    const created = makeDisputeDetail(
      {
        id: body.id,
        transactionId: transaction.id,
        merchantName: transaction.merchantName,
        amount: body.amount ?? transaction.amount,
        currency: transaction.currency,
        reasonCode: body.reasonCode,
        description,
        createdAt: at,
        updatedAt: at,
        timeline: [
          { from: null, to: "Submitted", actorType: "Customer", note: null, occurredAt: at },
        ],
        transaction: {
          id: transaction.id,
          reference: transaction.reference,
          merchantName: transaction.merchantName,
          amount: transaction.amount,
          currency: transaction.currency,
          transactionDate: transaction.transactionDate,
        },
      },
      store.length,
    );
    store.unshift(created);
    versions.set(created.id, 1);
    // The transaction now carries the dispute, as the real list would show it.
    transaction.dispute = { id: created.id, status: "Submitted" };
    transaction.isDisputable = false;
    transaction.disputeBlockReasons = ["already_disputed"];

    const lost = await options.afterCreate?.(created);
    if (lost) return lost;
    return HttpResponse.json(created, {
      status: 201,
      headers: { Location: `/api/v1/disputes/${created.id}`, ...detailHeaders(created.id) },
    });
  });

  const listHandler = http.get("/api/v1/disputes", ({ request }) => {
    const url = new URL(request.url);
    api.requests.push(url);
    const q = url.searchParams;
    const limit = Number(q.get("limit") ?? 25);
    const status = q.get("status");
    if (!Number.isInteger(limit) || limit < 1 || limit > 100) {
      return problem(400, {
        code: "validation_failed",
        errors: { limit: ["limit must be 1..100"] },
      });
    }
    if (
      status &&
      !["Submitted", "UnderReview", "Accepted", "Rejected", "Withdrawn"].includes(status)
    ) {
      return problem(400, { code: "validation_failed", errors: { status: ["unknown status"] } });
    }
    let offset = 0;
    const cursor = q.get("cursor");
    if (cursor) {
      const match = /^cursor:(\d+)$/.exec(safeAtob(cursor));
      if (!match)
        return problem(400, {
          title: "Bad Request",
          detail: "The cursor is not valid.",
          code: "invalid_cursor",
        });
      offset = Number(match[1]);
    }
    const matching = store.filter((d) => !status || d.status === status);
    const items = matching.slice(offset, offset + limit).map(toListItem);
    const next = offset + limit;
    return HttpResponse.json({
      items,
      nextCursor: next < matching.length ? btoa(`cursor:${next}`) : null,
    });
  });

  const getHandler = http.get("/api/v1/disputes/:id", async ({ params }) => {
    const id = String(params["id"]);
    api.requests.push(new URL(`/api/v1/disputes/${id}`, "http://localhost"));
    const intercepted = await options.interceptGet?.(id);
    if (intercepted) return intercepted;
    const found = store.find((d) => d.id === id);
    return found
      ? HttpResponse.json(found, { headers: detailHeaders(id) })
      : problem(404, { title: "Not Found", detail: "Dispute not found.", code: "not_found" });
  });

  const withdrawHandler = http.post(
    "/api/v1/disputes/:id/withdraw",
    async ({ params, request }) => {
      const id = String(params["id"]);
      const ifMatch = request.headers.get("if-match");
      api.withdrawCalls.push({ id, ifMatch });
      const intercepted = await options.interceptWithdraw?.(id);
      if (intercepted) return intercepted;
      const index = store.findIndex((d) => d.id === id);
      const current = store[index];
      if (!current)
        return problem(404, {
          title: "Not Found",
          detail: "Dispute not found.",
          code: "not_found",
        });
      if (ifMatch === null) {
        return problem(428, { title: "Precondition Required", code: "precondition_required" });
      }
      if (ifMatch !== etagOf(versions.get(id) ?? 1)) {
        return problem(
          412,
          { title: "Precondition Failed", code: "precondition_failed" },
          detailHeaders(id),
        );
      }
      if (!ACTIVE.includes(current.status)) {
        return problem(409, {
          title: "Conflict",
          detail: `A ${current.status} dispute cannot be withdrawn.`,
          code: "invalid_transition",
        });
      }
      const at = new Date(NOW + (counter += 1) * 60_000).toISOString();
      store[index] = {
        ...current,
        status: "Withdrawn",
        updatedAt: at,
        resolvedAt: at,
        timeline: [
          ...current.timeline,
          {
            from: current.status,
            to: "Withdrawn",
            actorType: "Customer",
            note: null,
            occurredAt: at,
          },
        ],
      };
      versions.set(id, (versions.get(id) ?? 1) + 1);
      return HttpResponse.json(store[index], { headers: detailHeaders(id) });
    },
  );

  return {
    api,
    handlers: [reasonsHandler, createHandler, listHandler, getHandler, withdrawHandler],
  };
}

function toListItem(detail: DisputeDetail) {
  return {
    id: detail.id,
    transactionId: detail.transactionId,
    merchantName: detail.merchantName,
    amount: detail.amount,
    currency: detail.currency,
    reasonCode: detail.reasonCode,
    status: detail.status,
    createdAt: detail.createdAt,
    updatedAt: detail.updatedAt,
    resolvedAt: detail.resolvedAt,
  };
}

function safeAtob(value: string): string {
  try {
    return atob(value);
  } catch {
    return "";
  }
}
