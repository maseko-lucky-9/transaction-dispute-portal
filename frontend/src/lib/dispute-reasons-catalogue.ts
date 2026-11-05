import type { DisputeReason } from "@/api/disputes";

/** "not_received" -> "Not received": a readable fallback for a code the catalogue lacks. */
export function humanizeCode(code: string): string {
  const words = code.replaceAll("_", " ").trim();
  return words ? words.charAt(0).toUpperCase() + words.slice(1) : code;
}

/** The customer-facing label for a reason code, from the catalogue when it has one. */
export function reasonLabel(code: string, catalogue: readonly DisputeReason[] | undefined): string {
  return catalogue?.find((reason) => reason.code === code)?.label ?? humanizeCode(code);
}
