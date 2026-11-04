import { useSyncExternalStore } from "react";

/**
 * Light/dark theme. With nothing stored the page follows `prefers-color-scheme` through CSS
 * alone (see index.css). The toggle pins an explicit choice via <html data-theme>, kept in
 * localStorage. That key holds a UI preference only: never auth state.
 *
 * Deliberately not `next-themes`: it injects an inline <script>, which the CSP
 * (script-src 'self') blocks.
 */
export type Theme = "light" | "dark";

const STORAGE_KEY = "dp-theme";
const CHANGE_EVENT = "dp-theme-change";
const DARK_QUERY = "(prefers-color-scheme: dark)";

function isTheme(value: unknown): value is Theme {
  return value === "light" || value === "dark";
}

function readStoredTheme(): Theme | null {
  try {
    const value = window.localStorage.getItem(STORAGE_KEY);
    return isTheme(value) ? value : null;
  } catch {
    return null; // storage blocked (private mode, disabled cookies)
  }
}

function systemTheme(): Theme {
  return window.matchMedia(DARK_QUERY).matches ? "dark" : "light";
}

/** The theme currently in effect. */
export function resolveTheme(): Theme {
  return readStoredTheme() ?? systemTheme();
}

/** Apply the stored choice (if any) to <html>. Call once before the first render. */
export function applyStoredTheme(): void {
  const stored = readStoredTheme();
  if (stored) document.documentElement.dataset["theme"] = stored;
  else delete document.documentElement.dataset["theme"];
}

export function setTheme(theme: Theme): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, theme);
  } catch {
    // Still apply for this page view; it just will not survive a reload.
  }
  document.documentElement.dataset["theme"] = theme;
  window.dispatchEvent(new Event(CHANGE_EVENT));
}

function subscribe(onChange: () => void): () => void {
  const media = window.matchMedia(DARK_QUERY);
  media.addEventListener("change", onChange);
  window.addEventListener(CHANGE_EVENT, onChange);
  return () => {
    media.removeEventListener("change", onChange);
    window.removeEventListener(CHANGE_EVENT, onChange);
  };
}

export function useTheme(): { theme: Theme; toggleTheme: () => void } {
  const theme = useSyncExternalStore(subscribe, resolveTheme, () => "light" as const);
  return { theme, toggleTheme: () => setTheme(theme === "dark" ? "light" : "dark") };
}
