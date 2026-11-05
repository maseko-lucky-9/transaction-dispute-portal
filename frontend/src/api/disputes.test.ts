import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";

import { problem } from "@/test/handlers";
import { makeDisputeDetail } from "@/test/disputes";
import { server } from "@/test/server";

import {
  createDispute,
  disputeKeys,
  disputesInfiniteOptions,
  fetchDispute,
  fetchDisputePage,
  fetchDisputeReasons,
  withdrawDispute,
} from "./disputes";
import { ApiError } from "./problem";

describe("fetchDisputeReasons", () => {
  it("reads the catalogue", async () => {
    server.use(
      http.get("/api/v1/dispute-reasons", () =>
        HttpResponse.json([{ code: "a", label: "A", description: "d" }]),
      ),
    );
    await expect(fetchDisputeReasons()).resolves.toEqual([
      { code: "a", label: "A", description: "d" },
    ]);
  });
});

describe("createDispute", () => {
  const body = {
    id: "11111111-1111-4111-8111-111111111111",
    transactionId: "t1",
    reasonCode: "unauthorised",
    description: "I did not make this purchase.",
  };

  it("posts the body as JSON and returns the dispute and its ETag (201 is not a replay)", async () => {
    let received: unknown;
    server.use(
      http.post("/api/v1/disputes", async ({ request }) => {
        received = await request.json();
        return HttpResponse.json(makeDisputeDetail({ id: body.id }), {
          status: 201,
          headers: { ETag: '"v1"', Location: `/api/v1/disputes/${body.id}` },
        });
      }),
    );

    const result = await createDispute(body);

    expect(received).toEqual(body);
    expect(result.dispute.id).toBe(body.id);
    expect(result.etag).toBe('"v1"');
    expect(result.replayed).toBe(false);
  });

  it("treats 200 as success and reports it as a replay of the same id", async () => {
    server.use(
      http.post("/api/v1/disputes", () =>
        HttpResponse.json(makeDisputeDetail({ id: body.id }), {
          status: 200,
          headers: { ETag: '"v3"' },
        }),
      ),
    );
    const result = await createDispute(body);
    expect(result.replayed).toBe(true);
    expect(result.etag).toBe('"v3"');
  });

  it("omits amount from the JSON when it is not set (full amount)", async () => {
    let received: Record<string, unknown> = {};
    server.use(
      http.post("/api/v1/disputes", async ({ request }) => {
        received = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(makeDisputeDetail(), { status: 201 });
      }),
    );
    await createDispute(body);
    expect(Object.keys(received)).not.toContain("amount");
  });

  it("returns a null ETag when the server sent none", async () => {
    server.use(
      http.post("/api/v1/disputes", () => HttpResponse.json(makeDisputeDetail(), { status: 201 })),
    );
    expect((await createDispute(body)).etag).toBeNull();
  });

  it("throws ApiError with the problem code on failure", async () => {
    server.use(
      http.post("/api/v1/disputes", () =>
        problem(409, { code: "dispute_exists", existingDisputeId: "d9", traceId: "t-9" }),
      ),
    );
    const error = (await createDispute(body).catch((e: unknown) => e)) as ApiError;
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 409, code: "dispute_exists", traceId: "t-9" });
    expect(error.problem["existingDisputeId"]).toBe("d9");
  });
});

describe("fetchDisputePage / disputesInfiniteOptions", () => {
  function capture() {
    const seen: URL[] = [];
    server.use(
      http.get("/api/v1/disputes", ({ request }) => {
        seen.push(new URL(request.url));
        return HttpResponse.json({ items: [], nextCursor: null });
      }),
    );
    return seen;
  }

  it("sends only the limit when no filter is set", async () => {
    const seen = capture();
    await fetchDisputePage({ limit: 25 }, undefined);
    expect(seen[0]?.search).toBe("?limit=25");
  });

  it("sends the status and the cursor, encoded", async () => {
    const seen = capture();
    await fetchDisputePage({ status: "UnderReview", limit: 10 }, "a+b/c==");
    expect(Object.fromEntries(seen[0]?.searchParams ?? [])).toEqual({
      limit: "10",
      status: "UnderReview",
      cursor: "a+b/c==",
    });
  });

  it("keys the list by its filters and follows the cursor", () => {
    expect(disputeKeys.list({ limit: 25 })).not.toEqual(
      disputeKeys.list({ limit: 25, status: "Accepted" }),
    );
    const options = disputesInfiniteOptions({ limit: 25 });
    const page = (nextCursor: string | null) => ({ items: [], nextCursor });
    expect(options.getNextPageParam(page("abc"), [], undefined, [])).toBe("abc");
    expect(options.getNextPageParam(page(null), [], undefined, [])).toBeUndefined();
  });
});

describe("fetchDispute", () => {
  it("returns the dispute with the ETag it was read with", async () => {
    server.use(
      http.get("/api/v1/disputes/:id", () =>
        HttpResponse.json(makeDisputeDetail(), { headers: { ETag: '"v7"' } }),
      ),
    );
    const result = await fetchDispute("d1");
    expect(result.etag).toBe('"v7"');
    expect(result.dispute.status).toBe("Submitted");
  });

  it("encodes the id so it cannot escape the path", async () => {
    let path = "";
    server.use(
      http.get("/api/v1/disputes/:id", ({ request }) => {
        path = new URL(request.url).pathname;
        return HttpResponse.json(makeDisputeDetail());
      }),
    );
    await fetchDispute("a/../b?x=1");
    expect(path).toBe("/api/v1/disputes/a%2F..%2Fb%3Fx%3D1");
  });
});

describe("withdrawDispute", () => {
  function captureWithdraw() {
    const seen: { ifMatch: string | null; method: string }[] = [];
    server.use(
      http.post("/api/v1/disputes/:id/withdraw", ({ request }) => {
        seen.push({ ifMatch: request.headers.get("if-match"), method: request.method });
        return HttpResponse.json(makeDisputeDetail({ status: "Withdrawn" }), {
          headers: { ETag: '"v2"' },
        });
      }),
    );
    return seen;
  }

  it("sends the ETag back in If-Match and returns the new ETag", async () => {
    const seen = captureWithdraw();
    const result = await withdrawDispute("d1", '"v1"');
    expect(seen).toEqual([{ ifMatch: '"v1"', method: "POST" }]);
    expect(result.etag).toBe('"v2"');
    expect(result.dispute.status).toBe("Withdrawn");
  });

  it("sends no If-Match at all when it has no ETag (the server will answer 428)", async () => {
    const seen = captureWithdraw();
    await withdrawDispute("d1", null);
    expect(seen[0]?.ifMatch).toBeNull();
  });

  it("surfaces 412 with the current ETag in the error's headers", async () => {
    server.use(
      http.post("/api/v1/disputes/:id/withdraw", () =>
        problem(412, { code: "precondition_failed" }, { ETag: '"v5"' }),
      ),
    );
    const error = (await withdrawDispute("d1", '"v1"').catch((e: unknown) => e)) as ApiError;
    expect(error).toMatchObject({ status: 412, code: "precondition_failed" });
    expect(error.headers.get("ETag")).toBe('"v5"');
  });
});
