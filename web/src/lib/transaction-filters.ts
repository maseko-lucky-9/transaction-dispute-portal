import { z } from "@/lib/zod";

import type { operations } from "@/api/schema";

/**
 * Filter rules for the transactions list. They mirror the API's query validation
 * (400 `validation_failed`), so a bad URL or form is caught before any request is sent.
 *
 *   limit   whole number 1..100, default 25
 *   from/to real calendar dates (yyyy-mm-dd) from 1900-01-01 to 2100-12-31, from <= to
 *   range   (to - from) <= 366 days (365 days = one year, 366 = one leap year)
 *   status  Pending | Posted | Reversed
 *
 * The same rules back two schemas with different failure behaviour:
 *  - `transactionsSearchSchema` (URL): never fails, falls back to defaults.
 *  - `filterFormSchema` (form): fails with a message per field.
 */

/** The query parameters of GET /api/v1/transactions, straight from the generated OpenAPI types. */
export type ListTransactionsQuery = NonNullable<
  operations["ListTransactions"]["parameters"]["query"]
>;

/** The API's status enum: the one source of truth for the type. */
export type TransactionStatus = NonNullable<ListTransactionsQuery["status"]>;

/**
 * The same values as a runtime list (the form's options and the zod enum need one; types do
 * not exist at runtime). `satisfies` rejects a value the API does not know, and the
 * "contract types" test fails `typecheck` if the API gains a status missing from this list.
 * The numeric limits below cannot come from types either; `contract.test.ts` pins them to the
 * committed `docs/openapi.json`, so they cannot drift from the API unnoticed.
 */
export const TRANSACTION_STATUSES = [
  "Pending",
  "Posted",
  "Reversed",
] as const satisfies readonly TransactionStatus[];

export const DEFAULT_LIMIT = 25;
export const MIN_LIMIT = 1;
export const MAX_LIMIT = 100;
export const MAX_RANGE_DAYS = 366;

const MS_PER_DAY = 86_400_000;
const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

/**
 * Supported dates. The server rejects anything outside (400): beyond 2100 it would overflow
 * its own "to + 1 day" arithmetic, and nothing before 1900 is a transaction.
 */
export const MIN_DATE = "1900-01-01";
export const MAX_DATE = "2100-12-31";

export type DateProblem = "format" | "bounds";

/** Why a yyyy-mm-dd string is not a usable filter date, or null when it is. */
export function dateProblem(value: string): DateProblem | null {
  const date = parseCalendarDate(value);
  if (!date) return "format";
  // Both ends are zero-padded yyyy-mm-dd, so string order is calendar order.
  return value < MIN_DATE || value > MAX_DATE ? "bounds" : null;
}

/** Parse a strict yyyy-mm-dd REAL calendar date (any year) to UTC midnight; null otherwise. */
function parseCalendarDate(value: string): Date | null {
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

/** Parse a usable filter date (real, and within MIN_DATE..MAX_DATE) to UTC midnight. */
export function parseIsoDate(value: string): Date | null {
  return dateProblem(value) === null ? parseCalendarDate(value) : null;
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

/** The filters the list is requested with: the API's parameters, with the page size always set. */
export type TransactionFilters = Pick<ListTransactionsQuery, "from" | "to" | "status"> &
  Required<Pick<ListTransactionsQuery, "limit">>;

// ---- URL ---------------------------------------------------------------------------------

/** A field that is dropped (not rejected) when it is not a real date within the bounds. */
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

export const DATE_PROBLEM_MESSAGES: Record<DateProblem, string> = {
  format: "Enter a valid date",
  bounds: `Enter a date between ${MIN_DATE.slice(0, 4)} and ${MAX_DATE.slice(0, 4)}`,
};

const formDate = z.string().superRefine((value, context) => {
  if (value === "") return; // optional
  const problem = dateProblem(value);
  if (problem) context.addIssue({ code: "custom", message: DATE_PROBLEM_MESSAGES[problem] });
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
