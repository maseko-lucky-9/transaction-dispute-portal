import { describe, expect, it } from "vitest";

import { ApiError, isApiError, toApiError } from "./problem";

function json(body: unknown, status: number, contentType = "application/problem+json") {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": contentType } });
}

describe("toApiError", () => {
  it("parses every ProblemDetails member", async () => {
    const error = await toApiError(
      json(
        {
          type: "https://errors.example/dispute-exists",
          title: "Dispute exists",
          status: 409,
          detail: "This transaction already has an active dispute.",
          instance: "/api/v1/disputes",
          code: "dispute_exists",
          traceId: "trace-1",
        },
        409,
      ),
    );

    expect(error.status).toBe(409);
    expect(error.message).toBe("This transaction already has an active dispute.");
    expect(error.code).toBe("dispute_exists");
    expect(error.traceId).toBe("trace-1");
    expect(error.problem.instance).toBe("/api/v1/disputes");
    expect(error.problem.type).toBe("https://errors.example/dispute-exists");
  });

  it("keeps extension members for the form pages (errors{}, violations[])", async () => {
    const error = await toApiError(
      json(
        { title: "Bad", errors: { description: ["too short"] }, violations: [{ rule: "window" }] },
        400,
      ),
    );
    expect(error.problem["errors"]).toEqual({ description: ["too short"] });
    expect(error.problem["violations"]).toEqual([{ rule: "window" }]);
  });

  it("treats null members as absent", async () => {
    const error = await toApiError(json({ title: "Oops", traceId: null, detail: null }, 500));
    expect(error.traceId).toBeUndefined();
    expect(error.message).toBe("Oops");
  });

  it("drops a wrong-typed member instead of failing the whole parse", async () => {
    const error = await toApiError(json({ title: "Oops", traceId: 12345, code: "x" }, 500));
    expect(error.traceId).toBeUndefined();
    expect(error.code).toBe("x");
    expect(error.problem.title).toBe("Oops");
  });

  it("trusts the HTTP status over the body's status", async () => {
    const error = await toApiError(json({ title: "Lies", status: 200 }, 500));
    expect(error.status).toBe(500);
    expect(error.problem.status).toBe(500);
  });

  it("falls back to the status text for a non-JSON body", async () => {
    const error = await toApiError(
      new Response("<html/>", {
        status: 503,
        statusText: "Service Unavailable",
        headers: { "Content-Type": "text/html" },
      }),
    );
    expect(error.status).toBe(503);
    expect(error.problem.title).toBe("Service Unavailable");
  });

  it("falls back to 'HTTP <status>' when there is no status text (HTTP/2)", async () => {
    const error = await toApiError(new Response(null, { status: 504 }));
    expect(error.problem.title).toBe("HTTP 504");
    expect(error.message).toBe("HTTP 504");
  });

  it.each([["null"], ["[1,2]"], ['"text"'], ["{broken"]])(
    "survives a JSON content type with a body that is not a problem object: %s",
    async (raw) => {
      const error = await toApiError(
        new Response(raw, { status: 500, headers: { "Content-Type": "application/json" } }),
      );
      expect(error.status).toBe(500);
      expect(error.traceId).toBeUndefined();
    },
  );
});

describe("ApiError", () => {
  it("builds a message from the detail, then the title, then the status", () => {
    expect(new ApiError(500, { title: "T", detail: "D" }).message).toBe("D");
    expect(new ApiError(500, { title: "T" }).message).toBe("T");
    expect(new ApiError(500, {}).message).toBe("Request failed (500)");
  });
});

describe("isApiError", () => {
  it("recognises ApiError only", () => {
    expect(isApiError(new ApiError(500, { title: "x" }))).toBe(true);
    expect(isApiError(new Error("x"))).toBe(false);
    expect(isApiError({ status: 500 })).toBe(false);
  });
});
