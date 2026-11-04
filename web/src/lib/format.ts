/**
 * Display formatting. The API sends money as decimal STRINGS ("1234.50") and UTC instants;
 * both stay strings until the last moment so nothing is rounded on the way to the screen.
 */

const LOCALE = "en-ZA";

const moneyFormats = new Map<string, Intl.NumberFormat>();

/**
 * "1234.5", "ZAR" -> "R 1 234,50". Intl formats decimal strings exactly: going through
 * Number() would turn 12345678901234567.89 into ...568.00.
 */
export function formatMoney(amount: string, currency: string): string {
  try {
    let format = moneyFormats.get(currency);
    if (!format) {
      format = new Intl.NumberFormat(LOCALE, { style: "currency", currency });
      moneyFormats.set(currency, format);
    }
    return format.format(amount as `${number}`);
  } catch {
    // Not a currency code Intl knows: still show the true amount, never nothing.
    return `${amount} ${currency}`;
  }
}

const dateFormat = new Intl.DateTimeFormat(LOCALE, { dateStyle: "medium", timeZone: "UTC" });

/**
 * Dates are shown in UTC, matching what the from/to filters mean on the server, so a row
 * dated 31 Oct always falls inside a filter ending 31 Oct. The column header says "UTC".
 */
export function formatDate(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : dateFormat.format(date);
}
