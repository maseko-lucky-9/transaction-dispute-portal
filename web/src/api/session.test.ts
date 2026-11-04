import { QueryClient } from "@tanstack/react-query";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";

import type * as Navigation from "@/lib/navigation";
import { navigate } from "@/lib/navigation";
import { alice, problem, signedIn, signedOut } from "@/test/handlers";
import { server } from "@/test/server";

import { ApiError } from "./problem";
import { createQueryClient } from "./query-client";
import { fetchSession, logout, sessionQueryKey } from "./session";

vi.mock("@/lib/navigation", async (importOriginal) => ({
  ...(await importOriginal<typeof Navigation>()),
  navigate: vi.fn(),
}));

describe("fetchSession", () => {
  it("returns the user on 200", async () => {
    server.use(signedIn());
    await expect(fetchSession()).resolves.toEqual(alice);
  });

  it("returns null on 401: not signed in is a state, not an error", async () => {
    server.use(signedOut());
    await expect(fetchSession()).resolves.toBeNull();
  });

  it("throws ApiError for other failures", async () => {
    server.use(http.get("/bff/user", () => problem(500)));
    await expect(fetchSession()).rejects.toBeInstanceOf(ApiError);
  });

  it("rejects a payload that breaks the contract", async () => {
    server.use(http.get("/bff/user", () => HttpResponse.json({ name: "Alice" })));
    await expect(fetchSession()).rejects.toThrow(/Unexpected response/);
  });

  it("rejects a logoutUrl that leaves the origin", async () => {
    server.use(signedIn({ ...alice, logoutUrl: "https://evil.example/logout" }));
    await expect(fetchSession()).rejects.toThrow(/Unexpected response/);
  });
});

describe("logout", () => {
  it("clears the query cache BEFORE the top-level navigation", () => {
    const queryClient = new QueryClient();
    queryClient.setQueryData(sessionQueryKey, alice);
    queryClient.setQueryData(["transactions"], [{ id: "t1" }]);

    let cachedWhenNavigating = -1;
    vi.mocked(navigate).mockImplementation(() => {
      cachedWhenNavigating = queryClient.getQueryCache().getAll().length;
    });

    logout(queryClient, alice.logoutUrl);

    expect(navigate).toHaveBeenCalledExactlyOnceWith("/bff/logout?sid=abc123");
    expect(cachedWhenNavigating).toBe(0);
  });
});

describe("createQueryClient", () => {
  it("flips the session to signed-out when any query gets a 401", async () => {
    const client = createQueryClient();
    client.setQueryData(sessionQueryKey, alice);
    server.use(http.get("/api/v1/transactions", () => problem(401, { title: "Unauthorized" })));

    await client
      .fetchQuery({
        queryKey: ["transactions"],
        queryFn: async () => {
          const { apiFetch } = await import("./client");
          return apiFetch("/api/v1/transactions");
        },
        retry: false,
      })
      .catch(() => undefined);

    expect(client.getQueryData(sessionQueryKey)).toBeNull();
  });

  it("flips the session to signed-out when a mutation gets a 401", async () => {
    const client = createQueryClient();
    client.setQueryData(sessionQueryKey, alice);

    await client
      .getMutationCache()
      .build(client, {
        mutationFn: () => Promise.reject(new ApiError(401, { title: "Unauthorized" })),
      })
      .execute(undefined)
      .catch(() => undefined);

    expect(client.getQueryData(sessionQueryKey)).toBeNull();
  });

  it("leaves the session alone for other errors", async () => {
    const client = createQueryClient();
    client.setQueryData(sessionQueryKey, alice);

    await client
      .fetchQuery({
        queryKey: ["x"],
        queryFn: () => Promise.reject(new ApiError(403, { title: "Forbidden" })),
        retry: false,
      })
      .catch(() => undefined);

    expect(client.getQueryData(sessionQueryKey)).toEqual(alice);
  });

  it("does not retry 4xx but retries a transient 5xx up to twice", () => {
    const retry = createQueryClient().getDefaultOptions().queries?.retry;
    if (typeof retry !== "function") throw new Error("expected a retry function");

    const clientError = new ApiError(404, { title: "Not found" });
    const serverError = new ApiError(503, { title: "Unavailable" });
    expect(retry(0, clientError)).toBe(false);
    expect(retry(0, serverError)).toBe(true);
    expect(retry(1, serverError)).toBe(true);
    expect(retry(2, serverError)).toBe(false);
    expect(retry(0, new Error("boom"))).toBe(true);
  });
});
