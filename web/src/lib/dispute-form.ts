import type { CreateDisputeRequest, DisputeDetail } from "@/api/disputes";
import { z } from "@/lib/zod";

import { compareDecimals, normalizeAmount } from "@/lib/decimal";
import { formatMoney } from "@/lib/format";
import { freeTextField } from "@/lib/free-text";

export const DESCRIPTION_MIN = 10;
export const DESCRIPTION_MAX = 2000;

export { INVALID_CHARACTERS_MESSAGE, SENSITIVE_MESSAGE } from "@/lib/free-text";

export interface DisputeFormRules {
  /** The codes the server offered; a reason outside the list is refused before any request. */
  reasonCodes: readonly string[];
  /** The transaction's amount: a dispute may not be for more. */
  maxAmount: string;
  currency: string;
}

export interface DisputeFormValues {
  reasonCode: string;
  description: string;
  /** Empty means the whole transaction. */
  amount: string;
}

/**
 * The same rules the API enforces (it stays authoritative), so a mistake is caught before a
 * request. The text is trimmed first: length is counted on what would actually be sent.
 */
export function disputeFormSchema(rules: DisputeFormRules) {
  return z.object({
    reasonCode: z
      .string()
      .min(1, "Choose a reason")
      .refine(
        (code) => code === "" || rules.reasonCodes.includes(code),
        "Choose a reason from the list",
      ),
    description: freeTextField({
      min: DESCRIPTION_MIN,
      max: DESCRIPTION_MAX,
      minMessage: `Describe what went wrong, in at least ${DESCRIPTION_MIN} characters`,
      maxMessage: `Keep the description to ${DESCRIPTION_MAX} characters or fewer`,
    }),
    amount: z
      .string()
      .trim()
      .superRefine((text, context) => {
        if (text === "") return; // optional: the full amount
        const amount = normalizeAmount(text);
        if (amount === null) {
          context.addIssue({
            code: "custom",
            message: "Enter an amount like 49.99, with at most 2 decimals",
          });
        } else if (compareDecimals(amount, "0") <= 0) {
          context.addIssue({ code: "custom", message: "The amount must be more than zero" });
        } else if (compareDecimals(amount, rules.maxAmount) > 0) {
          context.addIssue({
            code: "custom",
            message: `The amount cannot be more than ${formatMoney(rules.maxAmount, rules.currency)}`,
          });
        }
      }),
  });
}

/** The counter is announced only when it crosses one of these, so typing is never narrated. */
const ANNOUNCE_AT = [0, 10, 50, 100, 200] as const;

/** What a screen reader hears about the remaining length; empty while there is plenty of room. */
export function describeRemaining(length: number): string {
  const remaining = DESCRIPTION_MAX - length;
  if (remaining < 0) return "Over the limit";
  const threshold = ANNOUNCE_AT.find((at) => remaining <= at);
  if (threshold === undefined) return "";
  return threshold === 0 ? "No characters left" : `${threshold} or fewer characters left`;
}

/**
 * A replay (the same id sent again) is answered with the dispute AS IT WAS FIRST STORED, even if
 * the customer edited the form between attempts. True when what is stored is not what was just
 * sent: the text, the reason, or the amount (omitted means the whole transaction).
 */
export function keptEarlierDetails(
  stored: Pick<DisputeDetail, "description" | "reasonCode" | "amount"> & {
    transaction: Pick<DisputeDetail["transaction"], "amount">;
  },
  sent: Pick<CreateDisputeRequest, "description" | "reasonCode" | "amount">,
): boolean {
  return (
    stored.description.trim() !== sent.description.trim() ||
    stored.reasonCode !== sent.reasonCode ||
    compareDecimals(stored.amount, sent.amount ?? stored.transaction.amount) !== 0
  );
}
