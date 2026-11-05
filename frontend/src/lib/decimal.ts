/**
 * Money is a decimal STRING from the API to the screen and back; nothing here goes through a
 * float, so "0.10" + "0.20" style rounding can never decide whether an amount is allowed.
 */

/** Digits, then optionally a "." or "," and one or two decimals. Typed by people, not copied from code. */
const AMOUNT_INPUT = /^([0-9]+)(?:[.,]([0-9]{1,2}))?$/;

/** The API accepts at most this many digits before the point (its `amount` pattern). */
const MAX_WHOLE_DIGITS = 13;

/**
 * "50", "50.5" and "50,50" all become "50.00"; anything else (signs, thousands, 3 decimals, more
 * whole digits than the API takes) is null. What comes out always satisfies the API's pattern.
 */
export function normalizeAmount(input: string): string | null {
  const match = AMOUNT_INPUT.exec(input.trim());
  if (!match) return null;
  const whole = BigInt(match[1] ?? "0").toString(); // drops leading zeros
  if (whole.length > MAX_WHOLE_DIGITS) return null;
  return `${whole}.${(match[2] ?? "").padEnd(2, "0")}`;
}

/** Compares two non-negative decimal strings of any scale: -1, 0 or 1. */
export function compareDecimals(a: string, b: string): -1 | 0 | 1 {
  const [aWhole = "0", aFraction = ""] = a.split(".");
  const [bWhole = "0", bFraction = ""] = b.split(".");
  const scale = Math.max(aFraction.length, bFraction.length);
  const left = BigInt(aWhole + aFraction.padEnd(scale, "0"));
  const right = BigInt(bWhole + bFraction.padEnd(scale, "0"));
  return left < right ? -1 : left > right ? 1 : 0;
}
