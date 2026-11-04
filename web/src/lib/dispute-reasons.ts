/** Why a transaction cannot be disputed, as the API reports it in `disputeBlockReasons`. */
export type DisputeBlockReason = "not_posted" | "not_debit" | "window_expired";

const MESSAGES: Record<DisputeBlockReason, string> = {
  not_posted: "Pending transactions can't be disputed yet",
  not_debit: "Only debits can be disputed",
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
