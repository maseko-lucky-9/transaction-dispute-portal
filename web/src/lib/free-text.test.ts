import { describe, expect, it } from "vitest";

import { freeTextField, INVALID_CHARACTERS_MESSAGE, SENSITIVE_MESSAGE } from "./free-text";

const rules = { min: 3, max: 10, minMessage: "too short", maxMessage: "too long" };
const messages = (text: string, r = rules) => {
  const result = freeTextField(r).safeParse(text);
  return result.success ? [] : result.error.issues.map((issue) => issue.message);
};

describe("freeTextField", () => {
  it("trims before counting length", () => {
    expect(messages("   ab   ")).toEqual(["too short"]);
    expect(freeTextField(rules).parse("  abc  ")).toBe("abc");
    expect(messages("  1234567890  ")).toEqual([]);
    expect(messages("12345678901")).toEqual(["too long"]);
  });

  it("allows empty text when min is 0", () => {
    expect(messages("   ", { ...rules, min: 0 })).toEqual([]);
  });

  it("refuses control characters, even at the edge where trimming would hide them", () => {
    expect(messages("abc\u0000")).toContain(INVALID_CHARACTERS_MESSAGE);
    expect(messages("ab\u0007c")).toContain(INVALID_CHARACTERS_MESSAGE);
  });

  it("allows tab, line feed and carriage return", () => {
    expect(messages("a\tb\r\nc")).toEqual([]);
  });

  it("refuses a Luhn-valid card number, however it is spaced", () => {
    const big = { ...rules, max: 100 };
    expect(messages("4111 1111 1111 1111", big)).toEqual([SENSITIVE_MESSAGE]);
    expect(messages("ref 4111-1111-1111-1111 ok", big)).toEqual([SENSITIVE_MESSAGE]);
  });

  it("leaves a Luhn-invalid long number alone", () => {
    expect(messages("4111 1111 1111 1112", { ...rules, max: 100 })).toEqual([]);
  });
});
