import { delay, http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";

import { server } from "@/test/server";
import { problem } from "@/test/handlers";

import { apiFetch, apiRequest, CSRF_HEADER } from "./client";
import { ApiError } from "./problem";

/** Record the headers of every request that reaches `path`. */
function captureHeaders(method: "get" | "post" | "delete", path: string) {
  const seen: Headers[] = [];
  server.use(
    http[method](path, ({ request }) => {
      seen.push(request.headers);
      return HttpResponse.json({ ok: true });
    }),
  );
  return seen;
}

describe("apiFetch: CSRF and credentials", () => {
  it("sends X-CSRF: 1 on GET, POST and DELETE", async () => {
    const gets = captureHeaders("get", "/api/v1/a");
    const posts = captureHeaders("post", "/api/v1/b");
    const deletes = captureHeaders("delete", "/api/v1/c");

    await apiFetch("/api/v1/a");
    await apiFetch("/api/v1/b", { method: "POST", json: { x: 1 } });
    await apiFetch("/api/v1/c", { method: "DELETE" });

    for (const headers of [...gets, ...posts, ...deletes]) {
      expect(headers.get(CSRF_HEADER)).toBe("1");
    }
    expect(gets.length + posts.length + deletes.length).toBe(3);
  });

  it("does not let a caller override or drop the header", async () => {
    const seen = captureHeaders("get", "/api/v1/a");
    await apiFetch("/api/v1/a", { headers: { "X-CSRF": "0" } });
    await apiFetch("/api/v1/a", { headers: new Headers({ "x-csrf": "evil" }) });
    expect(seen.map((h) => h.get("x-csrf"))).toEqual(["1", "1"]);
  });

  it("uses same-origin credentials and never a Bearer token", async () => {
    const seen = captureHeaders("get", "/api/v1/a");
    const fetchSpy = vi.spyOn(globalThis, "fetch");

    await apiFetch("/api/v1/a");

    const init = fetchSpy.mock.calls[0]?.[1];
    expect(init?.credentials).toBe("same-origin");
    expect(seen[0]?.get("authorization")).toBeNull();
  });

  it("serialises json bodies with a JSON content type", async () => {
    let body: unknown;
    let contentType: string | null = null;
    server.use(
      http.post("/api/v1/b", async ({ request }) => {
        contentType = request.headers.get("content-type");
        body = await request.json();
        return HttpResponse.json({});
      }),
    );

    await apiFetch("/api/v1/b", { method: "POST", json: { reasonCode: "fraud" } });

    expect(contentType).toBe("application/json");
    expect(body).toEqual({ reasonCode: "fraud" });
  });
});

describe("apiFetch: same-origin only", () => {
  it.each(["https://evil.example/steal", "//evil.example/steal", "api/v1/relative"])(
    "refuses %s without sending anything",
    async (target) => {
      const fetchSpy = vi.spyOn(globalThis, "fetch");
      await expect(apiFetch(target)).rejects.toThrow(/same-origin/);
      expect(fetchSpy).not.toHaveBeenCalled();
    },
  );
});

describe("apiFetch: responses", () => {
  it("returns the parsed JSON body", async () => {
    server.use(http.get("/api/v1/a", () => HttpResponse.json({ id: 7 })));
    await expect(apiFetch<{ id: number }>("/api/v1/a")).resolves.toEqual({ id: 7 });
  });

  it("returns undefined for 204 No Content", async () => {
    server.use(http.delete("/api/v1/a", () => new HttpResponse(null, { status: 204 })));
    await expect(apiFetch("/api/v1/a", { method: "DELETE" })).resolves.toBeUndefined();
  });

  it("throws ApiError carrying the problem details, including the traceId", async () => {
    server.use(
      http.get("/api/v1/a", () =>
        problem(409, { code: "dispute_exists", title: "Dispute exists", traceId: "trace-409" }),
      ),
    );

    const error = await apiFetch("/api/v1/a").catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    const apiError = error as ApiError;
    expect(apiError.status).toBe(409);
    expect(apiError.code).toBe("dispute_exists");
    expect(apiError.traceId).toBe("trace-409");
    expect(apiError.problem.title).toBe("Dispute exists");
  });

  it("wraps a non-JSON error body (e.g. an nginx 502 page) in an ApiError", async () => {
    server.use(
      http.get(
        "/api/v1/a",
        () =>
          new HttpResponse("<html>Bad Gateway</html>", {
            status: 502,
            headers: { "Content-Type": "text/html" },
          }),
      ),
    );
    await expect(apiFetch("/api/v1/a")).rejects.toMatchObject({
      name: "ApiError",
      status: 502,
      traceId: undefined,
    });
  });

  it("reports a network failure as ApiError status 0", async () => {
    server.use(http.get("/api/v1/a", () => HttpResponse.error()));
    await expect(apiFetch("/api/v1/a")).rejects.toMatchObject({
      name: "ApiError",
      status: 0,
      code: "network_error",
    });
  });

  it("reports an unreadable 200 body as an ApiError", async () => {
    server.use(
      http.get(
        "/api/v1/a",
        () => new HttpResponse("not json", { headers: { "Content-Type": "application/json" } }),
      ),
    );
    await expect(apiFetch("/api/v1/a")).rejects.toMatchObject({ name: "ApiError", status: 200 });
  });

  it("lets an abort through untouched so React Query can cancel", async () => {
    server.use(
      http.get("/api/v1/a", async () => {
        await delay(200);
        return HttpResponse.json({});
      }),
    );
    const controller = new AbortController();
    const pending = apiFetch("/api/v1/a", { signal: controller.signal });
    controller.abort();
    await expect(pending).rejects.toMatchObject({ name: "AbortError" });
  });
});

describe("apiRequest: status and headers", () => {
  it("returns the body with the status and the response headers (ETag, Location)", async () => {
    server.use(
      http.post("/api/v1/disputes", () =>
        HttpResponse.json(
          { id: "d1" },
          { status: 201, headers: { ETag: '"v1"', Location: "/api/v1/disputes/d1" } },
        ),
      ),
    );

    const response = await apiRequest<{ id: string }>("/api/v1/disputes", {
      method: "POST",
      json: {},
    });

    expect(response.status).toBe(201);
    expect(response.data).toEqual({ id: "d1" });
    expect(response.headers.get("ETag")).toBe('"v1"');
    expect(response.headers.get("Location")).toBe("/api/v1/disputes/d1");
  });

  it("still sends X-CSRF and rejects off-origin paths, like apiFetch", async () => {
    const seen = captureHeaders("get", "/api/v1/a");
    await apiRequest("/api/v1/a", { headers: { "X-CSRF": "0" } });
    expect(seen[0]?.get("x-csrf")).toBe("1");
    await expect(apiRequest("https://evil.example/x")).rejects.toThrow(/same-origin/);
  });

  it("returns an undefined body for 204 with its headers", async () => {
    server.use(
      http.delete(
        "/api/v1/a",
        () => new HttpResponse(null, { status: 204, headers: { ETag: '"x"' } }),
      ),
    );
    const response = await apiRequest("/api/v1/a", { method: "DELETE" });
    expect(response.data).toBeUndefined();
    expect(response.headers.get("ETag")).toBe('"x"');
  });

  it("puts the failed response's headers on the ApiError (Retry-After, current ETag)", async () => {
    server.use(
      http.get("/api/v1/a", () =>
        HttpResponse.json(
          { title: "Too Many Requests", code: "rate_limited", traceId: "t-429" },
          {
            status: 429,
            headers: {
              "Content-Type": "application/problem+json",
              "Retry-After": "17",
              ETag: '"v9"',
            },
          },
        ),
      ),
    );
    const error = (await apiRequest("/api/v1/a").catch((e: unknown) => e)) as ApiError;
    expect(error).toBeInstanceOf(ApiError);
    expect(error.headers.get("Retry-After")).toBe("17");
    expect(error.headers.get("ETag")).toBe('"v9"');
  });

  it("gives a network failure empty headers", async () => {
    server.use(http.get("/api/v1/a", () => HttpResponse.error()));
    const error = (await apiRequest("/api/v1/a").catch((e: unknown) => e)) as ApiError;
    expect(error.status).toBe(0);
    expect([...error.headers.keys()]).toEqual([]);
  });
});
