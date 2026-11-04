import { afterEach, describe, expect, it, vi } from "vitest";

import { currentReturnUrl, isSameOriginUrl, loginUrl, navigate } from "./navigation";

function stubLocation(location: Partial<Location>) {
  vi.stubGlobal("location", {
    origin: "http://localhost:3000",
    pathname: "/",
    search: "",
    ...location,
  });
}

afterEach(() => vi.unstubAllGlobals());

describe("navigate", () => {
  it("is a top-level navigation via window.location.assign", () => {
    const assign = vi.fn();
    stubLocation({ assign });
    navigate("/bff/logout?sid=1");
    expect(assign).toHaveBeenCalledExactlyOnceWith("/bff/logout?sid=1");
  });
});

describe("currentReturnUrl / loginUrl", () => {
  it("returns path plus query string", () => {
    stubLocation({ pathname: "/transactions", search: "?status=open&limit=25" });
    expect(currentReturnUrl()).toBe("/transactions?status=open&limit=25");
  });

  it("never offers a protocol-relative path as a return target", () => {
    stubLocation({ pathname: "//evil.example/x", search: "?a=1" });
    expect(currentReturnUrl()).toBe("/");
  });

  it("URL-encodes the return url into the login url", () => {
    expect(loginUrl("/transactions?status=open&limit=25")).toBe(
      "/bff/login?returnUrl=%2Ftransactions%3Fstatus%3Dopen%26limit%3D25",
    );
  });
});

describe("isSameOriginUrl", () => {
  it.each(["/bff/logout?sid=1", "/", "http://localhost:3000/bff/logout"])("accepts %s", (url) => {
    expect(isSameOriginUrl(url)).toBe(true);
  });

  it.each([
    "https://evil.example/logout",
    "//evil.example/logout",
    "/\\evil.example",
    "javascript:alert(1)",
    "http://localhost:3000.evil.example/",
  ])("rejects %s", (url) => {
    expect(isSameOriginUrl(url)).toBe(false);
  });

  it("rejects an unparseable URL", () => {
    expect(isSameOriginUrl("http://[bad")).toBe(false);
  });
});
