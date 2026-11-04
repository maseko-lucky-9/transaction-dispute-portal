import { z } from "zod";

/**
 * Filter rules for the transactions list. They mirror the API's query validation
 * (400 `validation_failed`), so a bad URL or form is caught before any request is sent.
 *
 *   limit   whole number 1..100, default 25
 *   from/to real calendar dates (yyyy-mm-dd), from <= to
 *   range   (to - from) <= 366 days (365 days = one year, 366 = one leap year)
 *   status  Pending | Posted | Reversed
 *
 * The same rules back two schemas with different failure behaviour:
 *  - `transactionsSearchSchema` (URL): never fails, falls back to defaults.
 *  - `filterFormSchema` (form): fails with a message per field.
 */

export const TRANSACTION_STATUSES = ["Pending", "Posted", "Reversed"] as const;
export type TransactionStatus = (typeof TRANSACTION_STATUSES)[number];

export const DEFAULT_LIMIT = 25;
export const MIN_LIMIT = 1;
export const MAX_LIMIT = 100;
export const MAX_RANGE_DAYS = 366;

const MS_PER_DAY = 86_400_000;
const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

/** Parse a strict yyyy-mm-dd calendar date to UTC midnight; null for anything else. */
export function parseIsoDate(value: string): Date | null {
  const match = ISO_DATE.exec(value);
  if (!match) return null;
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  // Not Date.UTC(): it maps years 0-99 to 1900-1999. setUTCFullYear takes the year as given.
  const date = new Date(0);
  date.setUTCFullYear(year, month - 1, day);
  // The setter rolls 2026-02-30 over to March; a round trip catches it.
  const isRealDate =
    date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
  return isRealDate ? date : null;
}

export function isIsoDate(value: string): boolean {
  return parseIsoDate(value) !== null;
}

export type DateRangeProblem = "order" | "range";

/** What is wrong with a from/to pair, if anything. Missing or invalid ends are not a range. */
export function dateRangeProblem(from?: string, to?: string): DateRangeProblem | null {
  const start = from ? parseIsoDate(from) : null;
  const end = to ? parseIsoDate(to) : null;
  if (!start || !end) return null;
  const days = (end.getTime() - start.getTime()) / MS_PER_DAY;
  if (days < 0) return "order";
  if (days > MAX_RANGE_DAYS) return "range";
  return null;
}

export const DATE_RANGE_MESSAGES: Record<DateRangeProblem, string> = {
  order: "The end date must be on or after the start date",
  range: `Choose a range of ${MAX_RANGE_DAYS} days or less`,
};

export interface TransactionFilters {
  from?: string | undefined;
  to?: string | undefined;
  status?: TransactionStatus | undefined;
  limit: number;
}

// ---- URL ---------------------------------------------------------------------------------

/** A field that is dropped (not rejected) when it does not parse. */
const urlDate = z.string().refine(isIsoDate).optional().catch(undefined);

/**
 * `validateSearch` for /transactions. Every field falls back on its own, so no URL can crash
 * the page. An inconsistent pair (from after to, or a range over 366 days) drops BOTH dates:
 * guessing which end the user meant would silently show the wrong data.
 */
export const transactionsSearchSchema = z
  .object({
    from: urlDate,
    to: urlDate,
    status: z.enum(TRANSACTION_STATUSES).optional().catch(undefined),
    // `.catch` handles a bad value, `.default` makes the param optional for <Link search>.
    limit: z
      .number()
      .int()
      .min(MIN_LIMIT)
      .max(MAX_LIMIT)
      .catch(DEFAULT_LIMIT)
      .default(DEFAULT_LIMIT),
  })
  .transform((search) =>
    dateRangeProblem(search.from, search.to)
      ? { ...search, from: undefined, to: undefined }
      : search,
  );

// ---- Form --------------------------------------------------------------------------------

const formDate = z.string().refine((value) => value === "" || isIsoDate(value), {
  message: "Enter a valid date",
});

/** Values as the inputs hold them: "" means "not set". */
export const filterFormSchema = z
  .object({
    from: formDate,
    to: formDate,
    status: z.enum(["", ...TRANSACTION_STATUSES], { error: "Choose a status from the list" }),
  })
  .superRefine((values, context) => {
    const problem = dateRangeProblem(values.from, values.to);
    if (problem) {
      // On `to`: it is the field the user most likely adjusts, and the message names both.
      context.addIssue({ code: "custom", path: ["to"], message: DATE_RANGE_MESSAGES[problem] });
    }
  });

export type FilterFormValues = z.infer<typeof filterFormSchema>;
