import { describe, expect, inject, it } from "vitest";

import { contrastRatio } from "./contrast";

/**
 * The theme lives in src/index.css as `--name: light-dark(#light, #dark);`. Vitest blanks CSS
 * imports, so vite.config.ts reads the file in Node and injects it (like the OpenAPI snapshot).
 */
const css = inject("indexCss");

interface Pair {
  light: string;
  dark: string;
}

function token(name: string): Pair {
  const escaped = name.replace(/[-]/g, "\\-");
  const match = new RegExp(
    `^\\s*--${escaped}:\\s*light-dark\\((#[0-9a-fA-F]{6}),\\s*(#[0-9a-fA-F]{6})\\);`,
    "m",
  ).exec(css);
  if (!match?.[1] || !match[2]) {
    throw new Error(`--${name} is missing from index.css or is not "light-dark(#hex, #hex);"`);
  }
  return { light: match[1], dark: match[2] };
}

const STATUSES = ["submitted", "review", "accepted", "rejected", "withdrawn"] as const;

/** [text token, background token]: every pair must be readable text in both themes. */
const PAIRS: readonly (readonly [string, string])[] = [
  ["foreground", "background"],
  ["foreground", "card"],
  ["card-foreground", "card"],
  ["muted-foreground", "card"],
  ["muted-foreground", "background"],
  ["primary-foreground", "primary"],
  ["destructive-foreground", "destructive"],
  ["destructive", "card"],
  ["positive", "card"],
  ["brand-gem", "card"],
  ["brand-crimson", "card"],
  ...STATUSES.map((s) => [`status-${s}`, `status-${s}-bg`] as const),
];

describe("contrastRatio", () => {
  it("matches known WCAG values", () => {
    expect(contrastRatio("#000000", "#ffffff")).toBeCloseTo(21, 5);
    expect(contrastRatio("#ffffff", "#ffffff")).toBeCloseTo(1, 5);
    expect(contrastRatio("#2f70ef", "#ffffff")).toBeCloseTo(4.48, 2);
  });

  it("rejects anything that is not #rrggbb", () => {
    expect(() => contrastRatio("red", "#fff")).toThrow(/#rrggbb/);
  });
});

describe("theme colour contrast (WCAG AA, 4.5:1)", () => {
  describe.each(["light", "dark"] as const)("%s theme", (theme) => {
    it.each(PAIRS)("%s on %s", (text, background) => {
      const ratio = contrastRatio(token(text)[theme], token(background)[theme]);
      expect(ratio).toBeGreaterThanOrEqual(4.5);
    });
  });

  it("fails loudly when a token is missing", () => {
    expect(() => token("no-such-token")).toThrow(/missing/);
  });
});
