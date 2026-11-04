/**
 * Client-side mirror of the API's free-text rules (400 `invalid_characters` and 400
 * `sensitive_data`). The server stays authoritative; this only catches a mistake before the
 * request is sent. `validation-cases.json` pins the rule for both halves, and the backend reads
 * the same file.
 *
 * THE RULE, for a description, in this order:
 *
 *   A. `invalid_characters`: the text contains a control character (Unicode category Cc), other
 *      than tab, line feed or carriage return. Checked on the text exactly as typed.
 *
 *   B. `sensitive_data`: the text carries a card number or a South African ID number. Both are
 *      13-19 digit strings that pass the Luhn checksum. To find one:
 *        1. Normalise: NFKC, then delete every format character (category Cf, e.g. zero-width
 *           space), then turn every decimal digit (category Nd, any script) into its ASCII
 *           digit by its value.
 *        2. A "chain" is a maximal run of ASCII digit groups, where consecutive groups are
 *           separated by 1 to 4 characters, each a whitespace character, a dash (category
 *           Pd), or one of  . , / _ ( )
 *        3. Within a chain take every contiguous run of whole groups and concatenate its
 *           digits. The text is sensitive when any such concatenation has 13-19 digits and
 *           passes Luhn.
 *
 * Why whole groups and not every 13-19 digit window: sliding windows flag about two thirds of
 * random 16-digit numbers that FAIL Luhn (some sub-window passes by chance). Whole groups keep
 * "4111 1111 1111 1111" and "ref 1 4111111111111111" caught while leaving Luhn-invalid numbers
 * alone.
 *
 * Known limits: this deters ACCIDENTAL disclosure, not deliberate obfuscation. A gap of five or
 * more separators, words between the groups, or digits spelled out as words are not joined.
 * And "every digit of every script" means every digit this runtime's Unicode tables know: a
 * script added in a newer Unicode than the server's (Node 22 knows ten Sunuwar digits that
 * .NET 10 does not) is read here and not there. The server is authoritative, so that can only
 * make this side stricter, never let something through.
 *
 * Parity with .NET was checked code point by code point (Nd, Cf, Pd, Cc, NFKC, and each digit's
 * value against `CharUnicodeInfo.GetDecimalDigitValue`): the only differences are that Unicode
 * skew, and `\s` matching U+FEFF here and U+0085 there, both removed or refused before the chain
 * is scanned (the first is category Cf, the second Cc).
 */

export const SENSITIVE_MIN_DIGITS = 13;
export const SENSITIVE_MAX_DIGITS = 19;

/** Rule A: a control character that is not tab, line feed or carriage return. */
const FORBIDDEN_CONTROL = /(?![\t\n\r])\p{Cc}/u;

const FORMAT_CHARACTER = /\p{Cf}/gu;
const DECIMAL_DIGIT = /\p{Nd}/u;
const DECIMAL_DIGITS = /\p{Nd}/gu;

/**
 * Rule B, step 2: ASCII digit groups (explicit [0-9], never `\d`) joined by 1-4
 * separators. Digits and separators never overlap, so this cannot backtrack badly.
 */
const DIGIT_CHAIN = /[0-9]+(?:[\s\p{Pd}.,/_()]{1,4}[0-9]+)*/gu;
const NOT_A_DIGIT = /[^0-9]+/;

/**
 * The value (0-9) of a decimal digit in any script. JavaScript has no such API, so it is derived
 * from the code point: Unicode lays decimal digits out in contiguous runs of ten, zero first, so
 * the value is the distance from the start of the run, modulo ten. (The 50 mathematical digits
 * U+1D7CE-U+1D7FF form one run of five such sets, hence the modulo.) This is the value .NET's
 * `CharUnicodeInfo.GetDecimalDigitValue` reports.
 */
export function decimalDigitValue(character: string): number {
  const codePoint = character.codePointAt(0);
  if (codePoint === undefined || !DECIMAL_DIGIT.test(character)) return -1;
  let start = codePoint;
  while (DECIMAL_DIGIT.test(String.fromCodePoint(start - 1))) start--;
  return (codePoint - start) % 10;
}

/** Rule B, step 1: what the digit scan reads. Exported for tests. */
export function normalizeForDigitScan(text: string): string {
  return text
    .normalize("NFKC")
    .replace(FORMAT_CHARACTER, "")
    .replace(DECIMAL_DIGITS, (digit) => String(decimalDigitValue(digit)));
}

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

/** Rule A. */
export function containsForbiddenControlCharacter(text: string): boolean {
  return FORBIDDEN_CONTROL.test(text);
}

/** Rule B. */
export function containsSensitiveNumber(text: string): boolean {
  for (const [chain] of normalizeForDigitScan(text).matchAll(DIGIT_CHAIN)) {
    const groups = chain.split(NOT_A_DIGIT);
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

export type FreeTextProblem = "invalid_characters" | "sensitive_data";

/** Both rules, A first, as the server applies them: the code of the first one broken, or null. */
export function freeTextProblem(text: string): FreeTextProblem | null {
  if (containsForbiddenControlCharacter(text)) return "invalid_characters";
  if (containsSensitiveNumber(text)) return "sensitive_data";
  return null;
}
