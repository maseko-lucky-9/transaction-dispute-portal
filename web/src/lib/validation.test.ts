import { describe, expect, it } from "vitest";

import cases from "./validation-cases.json";
import {
  containsForbiddenControlCharacter,
  containsSensitiveNumber,
  decimalDigitValue,
  freeTextProblem,
  isLuhnValid,
  normalizeForDigitScan,
} from "./validation";

interface ValidationCase {
  name: string;
  input: string;
  valid: boolean;
  code: string | null;
}

describe("isLuhnValid", () => {
  it.each(["4111111111111111", "378282246310005", "8001015009087", "0", "00"])(
    "accepts %s",
    (digits) => {
      expect(isLuhnValid(digits)).toBe(true);
    },
  );

  it.each(["4111111111111112", "1", "1234567890123"])("rejects %s", (digits) => {
    expect(isLuhnValid(digits)).toBe(false);
  });

  it.each(["", "4111 1111", "4111-1111", "abc", "４１１１１１１１１１１１１１１１"])(
    "rejects non-digit input %j",
    (input) => {
      expect(isLuhnValid(input)).toBe(false);
    },
  );
});

describe("the free-text rules against the shared validation-cases.json", () => {
  const shared = cases as ValidationCase[];

  it("keeps the case file well formed", () => {
    expect(shared.length).toBeGreaterThan(10);
    for (const c of shared) {
      // A case is valid exactly when it carries no error code.
      expect(c.valid).toBe(c.code === null);
      expect([null, "sensitive_data", "invalid_characters"]).toContain(c.code);
    }
    expect(new Set(shared.map((c) => c.name)).size).toBe(shared.length);
  });

  it.each(shared.map((c) => [c.name, c] as const))("%s", (_name, c) => {
    expect(freeTextProblem(c.input)).toBe(c.code);
    // And each half on its own agrees with the code, so neither can hide behind the other.
    expect(containsForbiddenControlCharacter(c.input)).toBe(c.code === "invalid_characters");
    expect(containsSensitiveNumber(c.input)).toBe(c.code === "sensitive_data");
  });
});

describe("rule A: control characters", () => {
  const controls = [
    ...Array.from({ length: 0x20 }, (_, i) => i),
    ...Array.from({ length: 0x21 }, (_, i) => 0x7f + i), // U+007F-U+009F
  ];

  it.each(controls.map((code) => [code.toString(16).padStart(4, "0"), code] as const))(
    "U+%s is refused unless it is tab, line feed or carriage return",
    (_hex, code) => {
      const allowed = code === 0x09 || code === 0x0a || code === 0x0d;
      expect(containsForbiddenControlCharacter(`before${String.fromCharCode(code)}after`)).toBe(
        !allowed,
      );
    },
  );

  it("leaves other invisible characters to rule B (format characters are not control characters)", () => {
    for (const character of ["\u200b", "\u200d", "\ufeff", "\u00ad", "\u2028", "\u00a0"]) {
      expect(containsForbiddenControlCharacter(`a${character}b`)).toBe(false);
    }
  });

  it("is checked first: a text that breaks both rules reports invalid_characters", () => {
    expect(freeTextProblem("4111 1111 1111 1111\u0000")).toBe("invalid_characters");
    expect(freeTextProblem("4111 1111 1111 1111")).toBe("sensitive_data");
    expect(freeTextProblem("plain text\u0000")).toBe("invalid_characters");
    expect(freeTextProblem("plain text")).toBeNull();
  });
});

describe("rule B: separators", () => {
  const card = ["4111", "1111", "1111", "1111"];
  const separators: [string, string][] = [
    ["space", " "],
    ["tab", "\t"],
    ["line feed", "\n"],
    ["carriage return", "\r"],
    ["hyphen", "-"],
    ["en dash", "\u2013"],
    ["em dash", "\u2014"],
    ["minus-like hyphen (U+2010)", "\u2010"],
    ["fullwidth hyphen-minus", "\uff0d"],
    ["dot", "."],
    ["comma", ","],
    ["slash", "/"],
    ["underscore", "_"],
    ["parentheses", ")("],
    ["no-break space", "\u00a0"],
    ["ideographic space", "\u3000"],
    ["line separator", "\u2028"],
  ];

  it.each(separators)("joins groups separated by a %s", (_name, separator) => {
    expect(containsSensitiveNumber(card.join(separator))).toBe(true);
  });

  it.each(separators)(
    "a gap of four %s characters still joins, five does not",
    (_name, separator) => {
      const four = separator.length === 1 ? separator.repeat(4) : separator.repeat(2);
      const five =
        separator.length === 1 ? separator.repeat(5) : separator.repeat(2) + separator[0];
      expect(containsSensitiveNumber(card.join(four))).toBe(true);
      // Five apart: the chain ends, and no 13 digit run is left.
      expect(containsSensitiveNumber(card.join(five))).toBe(false);
    },
  );

  it("mixes separators within the limit", () => {
    expect(containsSensitiveNumber("4111 -\n1111 . 1111 (1111")).toBe(true);
  });

  it("does not let a letter or any other symbol join groups", () => {
    for (const other of ["a", ":", ";", "+", "#", "*", "|", "\\", "'", '"', "x", "\u2022"]) {
      expect(containsSensitiveNumber(card.join(other))).toBe(false);
    }
  });

  it("keeps separated numbers apart when each is too short on its own", () => {
    expect(containsSensitiveNumber("12.50 and 4111-1111")).toBe(false);
  });
});

describe("rule B: normalisation", () => {
  it("removes format characters between and inside groups", () => {
    expect(normalizeForDigitScan("41\u200b11\u200d11\ufeff11")).toBe("41111111");
    expect(containsSensitiveNumber("4111\u200b1111\u200b1111\u200b1111")).toBe(true);
    expect(containsSensitiveNumber("41\u200b11111111111111")).toBe(true);
  });

  it("applies NFKC: compatibility forms become their plain equivalents", () => {
    expect(normalizeForDigitScan("\uff14\uff11")).toBe("41"); // fullwidth digits
    expect(normalizeForDigitScan("\ufb01")).toBe("fi"); // ligature
    expect(normalizeForDigitScan("\u2460")).toBe("\u2460".normalize("NFKC")); // circled digit: not Nd
  });

  it("reads a digit written in any script by its value", () => {
    const arabic =
      "\u0664\u0661\u0661\u0661 \u0661\u0661\u0661\u0661 \u0661\u0661\u0661\u0661 \u0661\u0661\u0661\u0661";
    expect(normalizeForDigitScan(arabic)).toBe("4111 1111 1111 1111");
    expect(containsSensitiveNumber(arabic)).toBe(true);
  });

  it("joins digits of mixed scripts, and still applies Luhn to them", () => {
    // 4111 1111 1111 1111 with a digit or two of each group written in another script.
    expect(containsSensitiveNumber("\u0664111 1\u0661\u0661\u0661 11\u0661\u0661 111\u0661")).toBe(
      true,
    );
    // The same with the last digit 2: no longer a card number.
    expect(containsSensitiveNumber("\u0664111 1\u0661\u0661\u0661 11\u0661\u0661 111\u0662")).toBe(
      false,
    );
  });

  it("leaves text without digits, and ordinary numbers, alone", () => {
    expect(normalizeForDigitScan("no digits here")).toBe("no digits here");
    expect(containsSensitiveNumber("Charged R1200.50 on 2026-11-04 at 14:30")).toBe(false);
  });
});

describe("decimalDigitValue", () => {
  // Value of each digit four, one from each script, as .NET's GetDecimalDigitValue reports them.
  const fours: [string, string][] = [
    ["ASCII", "4"],
    ["Arabic-Indic", "\u0664"],
    ["Extended Arabic-Indic (Persian)", "\u06f4"],
    ["Devanagari", "\u096a"],
    ["Bengali", "\u09ea"],
    ["Thai", "\u0e54"],
    ["Lao", "\u0ed4"],
    ["Tibetan", "\u0f24"],
    ["Khmer", "\u17e4"],
    ["Mongolian", "\u1814"],
    ["Fullwidth", "\uff14"],
    ["Osmanya (astral)", "\u{104a4}"],
    ["Mathematical bold (astral)", "\u{1d7d2}"],
    ["Mathematical double-struck (astral)", "\u{1d7dc}"],
    ["Mathematical sans-serif (astral)", "\u{1d7e6}"],
    ["Mathematical monospace (astral)", "\u{1d7fa}"],
    ["Adlam (astral)", "\u{1e954}"],
  ];

  it.each(fours)("reads %s four as 4", (_script, character) => {
    expect(decimalDigitValue(character)).toBe(4);
  });

  it("reads the first and last digit of a run as 0 and 9", () => {
    expect(decimalDigitValue("\u0660")).toBe(0);
    expect(decimalDigitValue("\u0669")).toBe(9);
    expect(decimalDigitValue("\u{1d7ce}")).toBe(0);
    expect(decimalDigitValue("\u{1d7ff}")).toBe(9); // the 50th mathematical digit
    expect(decimalDigitValue("\u{1d7d8}")).toBe(0); // a new set of ten inside the same run
  });

  it("is -1 for anything that is not a decimal digit", () => {
    for (const character of ["a", " ", "\u00b2", "\u2460", "\u2155", "\u4e00", ""]) {
      expect(decimalDigitValue(character)).toBe(-1);
    }
  });

  describe("over every decimal digit Unicode has", () => {
    const digits: string[] = [];
    for (let codePoint = 0; codePoint <= 0x10ffff; codePoint++) {
      if (codePoint >= 0xd800 && codePoint <= 0xdfff) continue;
      const character = String.fromCodePoint(codePoint);
      if (/\p{Nd}/u.test(character)) digits.push(character);
    }

    it("finds a realistic number of them", () => {
      expect(digits.length).toBeGreaterThan(600);
    });

    it("counts 0-9 through every run, in order, from zero", () => {
      let previous: number | null = null;
      let runLength = 0;
      for (const character of digits) {
        const codePoint = character.codePointAt(0) ?? 0;
        const value = decimalDigitValue(character);
        expect(value).toBeGreaterThanOrEqual(0);
        expect(value).toBeLessThanOrEqual(9);
        if (previous !== null && previous === codePoint - 1) {
          runLength++;
        } else {
          if (previous !== null) expect(runLength % 10).toBe(0); // a whole number of sets of ten
          expect(value).toBe(0); // every run starts at zero
          runLength = 1;
        }
        if (runLength > 1)
          expect(value).toBe((decimalDigitValue(String.fromCodePoint(codePoint - 1)) + 1) % 10);
        previous = codePoint;
      }
    });

    it("agrees with NFKC wherever NFKC itself maps the digit to an ASCII digit", () => {
      let compared = 0;
      for (const character of digits) {
        const plain = character.normalize("NFKC");
        if (/^[0-9]$/.test(plain)) {
          expect(decimalDigitValue(character)).toBe(Number(plain));
          compared++;
        }
      }
      expect(compared).toBeGreaterThan(50); // fullwidth and the mathematical digits
    });
  });
});

describe("performance", () => {
  it.each([
    ["single digits and spaces", "1 ".repeat(1000)],
    ["single digits and dots", "1.".repeat(1000)],
    ["groups of four and slashes", "1111/".repeat(400)],
    ["one very long digit run", "1".repeat(2000)],
    ["a long run of separators", "1" + " ".repeat(1900) + "1"],
    ["format characters everywhere", "1\u200b".repeat(1000)],
  ])("stays fast on a maximum-length description of %s", (_name, text) => {
    const started = performance.now();
    freeTextProblem(text);
    expect(performance.now() - started).toBeLessThan(250);
  });
});
