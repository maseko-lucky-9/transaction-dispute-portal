import { describe, expect, it } from "vitest";

import { compareDecimals, normalizeAmount } from "./decimal";

describe("normalizeAmount", () => {
  it.each([
    ["50", "50.00"],
    ["50.5", "50.50"],
    ["50.55", "50.55"],
    ["50,5", "50.50"], // South African locale writes a decimal comma
    ["0.01", "0.01"],
    ["007", "7.00"],
    ["  12.30  ", "12.30"],
    ["9999999999999.99", "9999999999999.99"], // 13 whole digits is the most the API takes
    ["00000000000000000000007", "7.00"],
  ])("reads %j as %j", (input, expected) => {
    expect(normalizeAmount(input)).toBe(expected);
  });

  it.each([
    "",
    " ",
    "abc",
    "-5",
    "+5",
    "1.234",
    "1,234.56",
    "1 000",
    ".5",
    "5.",
    "1e3",
    "1..2",
    "1.2.3",
    "５",
  ])("rejects %j", (input) => {
    expect(normalizeAmount(input)).toBeNull();
  });
});

describe("compareDecimals", () => {
  it.each([
    ["1.00", "1.00", 0],
    ["1.00", "1", 0],
    ["123.45", "123.4500", 0],
    ["123.46", "123.45", 1],
    ["123.44", "123.45", -1],
    ["0.30", "0.3", 0],
    ["0.10", "0.09", 1],
    ["10", "9.99", 1],
    ["9.99", "10", -1],
    ["0", "0.00", 0],
    // Beyond a double's 15-17 significant digits: a float comparison calls these equal.
    ["9007199254740993.01", "9007199254740993.02", -1],
    ["12345678901234567890.12", "12345678901234567890.11", 1],
  ])("compares %s with %s as %i", (a, b, expected) => {
    expect(compareDecimals(a, b)).toBe(expected);
  });
});
