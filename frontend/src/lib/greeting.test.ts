import { describe, expect, it } from "vitest";

import { firstNameOf, greetingFor, homeGreeting } from "./greeting";

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

describe("homeGreeting", () => {
  it("adds the first name after a comma", () => {
    expect(homeGreeting(9, "Thandi Nkosi")).toBe("Good morning, Thandi");
  });

  it.each(["", "   "])("drops the comma when the name %j has no first word", (name) => {
    expect(homeGreeting(20, name)).toBe("Good evening");
  });
});
