import { http, HttpResponse } from "msw";

import type { AgentDisputeDetail, TransitionRequest } from "@/api/agent";
import type { DisputeStatus } from "@/api/disputes";
import { freeTextProblem } from "@/lib/validation";

import { problem } from "./handlers";
import { makeDisputeDetail, reasons } from "./disputes";

const AGENT = "aaaaaaaa-0000-4000-8000-000000000001";
const OPEN: DisputeStatus[] = ["Submitted", "UnderReview"];
const NEXT: Partial<Record<DisputeStatus, DisputeStatus[]>> = {
  Submitted: ["UnderReview"],
  UnderReview: ["Accepted", "Rejected"],
};

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
    ...overrides,
  };
}

export interface FakeAgentApi {
  readonly store: AgentDisputeDetail[];
  readonly queueRequests: URL[];
  readonly transitionCalls: { id: string; ifMatch: string | null; body: TransitionRequest }[];
  /** Another agent acts: the ETag moves on. */
  otherAgentActs(id: string, to: DisputeStatus): void;
}

const etagOf = (version: number) => `"v${version}"`;

/** The agent endpoints with the server's rules: 428 / 412 / 409 / 400, ETag on every read. */
export function fakeAgentApi(initial: AgentDisputeDetail[]) {
  const store = [...initial];
  const versions = new Map(store.map((d) => [d.id, 1]));
  const api: FakeAgentApi = {
    store,
    queueRequests: [],
    transitionCalls: [],
    otherAgentActs(id, to) {
      apply(id, to, "Handled elsewhere");
    },
  };

  function apply(id: string, to: DisputeStatus, note: string | null) {
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
      timeline: [
        ...current.timeline,
        { from: current.status, to, actorType: "Agent", actorId: AGENT, note, occurredAt: at },
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
  return { api, handlers: [reasonList, queue, get, transition] };
}
