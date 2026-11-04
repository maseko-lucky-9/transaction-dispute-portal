/** Why a transaction cannot be disputed, as the API reports it in `disputeBlockReasons`. */
export type DisputeBlockReason = "not_posted" | "not_debit" | "window_expired";

const MESSAGES: Record<DisputeBlockReason, string> = {
  not_posted: "Pending transactions can't be disputed yet",
  not_debit: "Only debits can be disputed",
  // "120" mirrors the server's DisputePolicyOptions.WindowDays default. The API sends only the
  // reason code, never the window length, so this number cannot follow a configuration change:
  // if WindowDays is ever changed, change it here too (the e2e suite asserts this exact text),
  // or drop the number and use "The dispute window has closed".
  window_expired: "The 120-day dispute window has closed",
};

const FALLBACK = "This transaction can't be disputed";

/** Friendly sentences for a transaction's block reasons; never empty for a blocked row. */
export function describeBlockReasons(reasons: readonly string[]): string[] {
  const sentences = reasons.map((reason) =>
    Object.hasOwn(MESSAGES, reason) ? MESSAGES[reason as DisputeBlockReason] : FALLBACK,
  );
  return sentences.length > 0 ? [...new Set(sentences)] : [FALLBACK];
}
