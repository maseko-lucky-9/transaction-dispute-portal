import { containsForbiddenControlCharacter, containsSensitiveNumber } from "@/lib/validation";
import { z } from "@/lib/zod";

export const SENSITIVE_MESSAGE = "Please remove card or ID numbers";
export const INVALID_CHARACTERS_MESSAGE = "Please remove control or non-printing characters";

interface FreeTextRules {
  /** Shortest allowed length after trimming; 0 means the text may be empty. */
  min: number;
  max: number;
  minMessage: string;
  maxMessage: string;
}

/**
 * THE free-text rule, for every field a person types into that the bank stores (a dispute's
 * description, an agent's resolution note): no control characters (checked on the text as typed,
 * first, because trimming would hide one at the edge), the length counted on the trimmed text,
 * and no card or ID number. The server applies the same rules; this catches them before a request.
 */
export function freeTextField(rules: FreeTextRules) {
  const base = z
    .string()
    .refine((text) => !containsForbiddenControlCharacter(text), INVALID_CHARACTERS_MESSAGE)
    .trim();
  const sized = (rules.min > 0 ? base.min(rules.min, rules.minMessage) : base).max(
    rules.max,
    rules.maxMessage,
  );
  return sized.refine((text) => !containsSensitiveNumber(text), SENSITIVE_MESSAGE);
}
