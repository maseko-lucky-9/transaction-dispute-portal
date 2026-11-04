/**
 * Client-side mirror of the API's `sensitive_data` rule (400 `sensitive_data`).
 *
 * Free-text fields must not carry a card number or a South African ID number. Both are
 * 13-19 digit strings that satisfy the Luhn checksum. The server stays authoritative; this
 * only catches the mistake before the request is sent.
 *
 * THE RULE (the backend implements exactly this; `validation-cases.json` pins it):
 *   1. A "chain" is a maximal run of ASCII digits [0-9] whose digit groups are separated by
 *      one or more of: space, tab, CR, LF, hyphen. Anything else (letters, "." "," ":" "/")
 *      ends the chain.
 *   2. Inside a chain, take every contiguous run of whole digit groups. Concatenate its
 *      digits.
 *   3. The text is sensitive when any such concatenation has 13-19 digits and passes Luhn.
 *
 * Why whole groups and not every 13-19 digit window: sliding windows flag about two thirds of
 * random 16-digit numbers that FAIL Luhn (some sub-window passes by chance). Whole groups keep
 * "4111 1111 1111 1111" and "ref 1 4111111111111111" caught while leaving
 * Luhn-invalid numbers alone.
 *
 * Known limit: this deters accidental disclosure, not deliberate obfuscation (non-ASCII
 * digits, ".", or digits split by words are not joined).
 */

export const SENSITIVE_MIN_DIGITS = 13;
export const SENSITIVE_MAX_DIGITS = 19;

/** Explicit [0-9]: `\d` is Unicode-aware in some regex engines (.NET) and must not be used. */
const DIGIT_CHAIN = /[0-9]+(?:[ \t\r\n-]+[0-9]+)*/g;
const GROUP_SEPARATOR = /[ \t\r\n-]+/;

/** Luhn checksum over a string of ASCII digits. Returns false for anything else. */
export function isLuhnValid(digits: string): boolean {
  if (!/^[0-9]+$/.test(digits)) return false;
  let sum = 0;
  let double = false;
  for (let i = digits.length - 1; i >= 0; i--) {
    let value = digits.charCodeAt(i) - 48;
    if (double) {
      value *= 2;
      if (value > 9) value -= 9;
    }
    sum += value;
    double = !double;
  }
  return sum % 10 === 0;
}

/** True when `text` contains a Luhn-valid 13-19 digit number (see the rule above). */
export function containsSensitiveNumber(text: string): boolean {
  for (const [chain] of text.matchAll(DIGIT_CHAIN)) {
    const groups = chain.split(GROUP_SEPARATOR);
    for (let start = 0; start < groups.length; start++) {
      let digits = "";
      for (let end = start; end < groups.length; end++) {
        digits += groups[end];
        if (digits.length > SENSITIVE_MAX_DIGITS) break;
        if (digits.length >= SENSITIVE_MIN_DIGITS && isLuhnValid(digits)) return true;
      }
    }
  }
  return false;
}
