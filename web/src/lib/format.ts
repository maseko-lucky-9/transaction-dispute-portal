/**
 * Display formatting. The API sends money as decimal STRINGS ("1234.5000") and UTC instants;
 * both stay strings until the last moment so nothing is rounded on the way to the screen.
 */

const LOCALE = "en-ZA";

/** A plain decimal string: optional sign, digits, optional fraction. No exponent, no spaces. */
const DECIMAL = /^-?\d+(?:\.(\d+))?$/;

/** Intl accepts up to 100 fraction digits in current engines but only 20 in older ones. */
const MAX_FRACTION_DIGITS = 20;

const moneyFormats = new Map<string, Intl.NumberFormat>();

function moneyFormat(currency: string, fractionDigits: number): Intl.NumberFormat {
  const key = `${currency}:${fractionDigits}`;
  let format = moneyFormats.get(key);
  if (!format) {
    format = new Intl.NumberFormat(LOCALE, {
      style: "currency",
      currency,
      minimumFractionDigits: fractionDigits,
      maximumFractionDigits: fractionDigits,
    });
    moneyFormats.set(key, format);
  }
  return format;
}

/** The currency's own minor-unit digits: 2 for ZAR/USD, 0 for JPY, 3 for KWD. */
function minorUnitDigits(currency: string): number {
  const resolved = new Intl.NumberFormat(LOCALE, { style: "currency", currency }).resolvedOptions();
  // The lib types mark it optional; for style "currency" engines always resolve it.
  return resolved.maximumFractionDigits ?? 2;
}

/**
 * "1234.5", "ZAR" -> "R 1 234,50".
 *
 * Nothing is rounded. Intl formats a decimal string exactly (going through Number() would
 * turn 12345678901234567.89 into ...568.00), and the fraction length follows what the API
 * sent: the currency's usual digits (2 for rand), more only when they are significant, so
 * "123.4567" shows all four digits while "10.5000" stays "10,50".
 */
export function formatMoney(amount: string, currency: string): string {
  const match = DECIMAL.exec(amount);
  // Not a plain decimal (or not a currency Intl knows): show the true value, never a guess.
  if (!match) return `${amount} ${currency}`;

  const significant = (match[1] ?? "").replace(/0+$/, "").length;
  try {
    const digits = Math.min(Math.max(significant, minorUnitDigits(currency)), MAX_FRACTION_DIGITS);
    return moneyFormat(currency, digits).format(amount as `${number}`);
  } catch {
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

const dateTimeFormat = new Intl.DateTimeFormat(LOCALE, {
  dateStyle: "medium",
  timeStyle: "short",
  timeZone: "UTC",
});

/** "4 Nov 2026, 09:00 UTC": a timeline needs the time, and it must say which clock it is. */
export function formatDateTime(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : `${dateTimeFormat.format(date)} UTC`;
}
