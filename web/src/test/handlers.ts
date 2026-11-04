import { http, HttpResponse } from "msw";

import type { ProblemDetails } from "@/api/problem";
import type { User } from "@/api/types";

export const alice: User = {
  name: "Alice Customer",
  roles: ["customer"],
  logoutUrl: "/bff/logout?sid=abc123",
};

export function signedIn(user: User = alice) {
  return http.get("/bff/user", () => HttpResponse.json(user));
}

export function signedOut() {
  return http.get("/bff/user", () => new HttpResponse(null, { status: 401 }));
}

/** An RFC 9457 response as the API sends it. */
export function problem(status: number, body: ProblemDetails = {}) {
  return HttpResponse.json(
    {
      type: "about:blank",
      title: "Internal Server Error",
      status,
      detail: "Something broke on our side.",
      instance: "/bff/user",
      code: "internal_error",
      traceId: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
      ...body,
    },
    { status, headers: { "Content-Type": "application/problem+json" } },
  );
}
