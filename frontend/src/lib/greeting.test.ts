import { describe, expect, it } from "vitest";

import { firstNameOf, greetingFor } from "./greeting";

describe("greetingFor", () => {
  it.each([
    [0, "Good morning"],
    [11, "Good morning"],
    [12, "Good afternoon"],
    [17, "Good afternoon"],
    [18, "Good evening"],
    [23, "Good evening"],
  ])("hour %i is %s", (hour, expected) => {
    expect(greetingFor(hour)).toBe(expected);
  });
});

describe("firstNameOf", () => {
  it.each([
    ["Thandi Nkosi", "Thandi"],
    ["  Thandi   Nkosi ", "Thandi"],
    ["Thandi", "Thandi"],
    ["", ""],
  ])("%j gives %j", (name, expected) => {
    expect(firstNameOf(name)).toBe(expected);
  });
});
