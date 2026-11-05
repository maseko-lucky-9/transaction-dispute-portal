/**
 * Why a transaction cannot be disputed (`disputeBlockReasons` on a transaction) and why a
 * dispute was refused (`violations[].code` on a 422). The API sends codes; the words are ours.
 */
export type DisputeBlockReason = "not_posted" | "not_debit" | "window_expired" | "already_disputed";

export type DisputeViolationCode =
  | DisputeBlockReason
  | "amount_exceeds_transaction"
  | "amount_not_positive"
  | "too_many_active_disputes"
  | "too_many_disputes_for_transaction";

const MESSAGES: Record<DisputeViolationCode, string> = {
  not_posted: "Pending transactions can't be disputed yet",
  not_debit: "Only debits can be disputed",
  // "120" mirrors the server's DisputePolicyOptions.WindowDays default. The API sends only the
  // reason code, never the window length, so this number cannot follow a configuration change:
  // if WindowDays is ever changed, change it here too (the e2e suite asserts this exact text),
  // or drop the number and use "The dispute window has closed".
  window_expired: "The 120-day dispute window has closed",
  already_disputed: "This transaction already has a dispute",
  amount_exceeds_transaction: "The amount is more than the transaction",
  amount_not_positive: "The amount must be more than zero",
  too_many_active_disputes:
    "You have too many open disputes. Wait for some to be decided, or withdraw one",
  too_many_disputes_for_transaction: "This transaction has reached the most disputes it can have",
};

const FALLBACK = "This transaction can't be disputed";

/** One rule, in plain language. A code we do not know falls back to the server's own sentence. */
export function describeViolation(code: string, serverMessage?: string): string {
  if (Object.hasOwn(MESSAGES, code)) return MESSAGES[code as DisputeViolationCode];
  return serverMessage?.trim() || FALLBACK;
}

/** Friendly sentences for a transaction's block reasons; never empty for a blocked row. */
export function describeBlockReasons(reasons: readonly string[]): string[] {
  const sentences = reasons.map((reason) => describeViolation(reason));
  return sentences.length > 0 ? [...new Set(sentences)] : [FALLBACK];
}
