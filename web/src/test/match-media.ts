import { vi } from "vitest";

type Listener = () => void;

export interface FakeMediaQuery {
  /** Flip the OS colour scheme and notify subscribers, like the OS toggling dark mode. */
  setDark(dark: boolean): void;
}

/** Replace window.matchMedia with a controllable `prefers-color-scheme: dark` query. */
export function mockColorScheme(dark: boolean): FakeMediaQuery {
  let matches = dark;
  const listeners = new Set<Listener>();

  vi.stubGlobal("matchMedia", (query: string) => ({
    get matches() {
      return query.includes("dark") ? matches : false;
    },
    media: query,
    addEventListener: (_type: string, listener: Listener) => listeners.add(listener),
    removeEventListener: (_type: string, listener: Listener) => listeners.delete(listener),
  }));

  return {
    setDark(next) {
      matches = next;
      listeners.forEach((listener) => listener());
    },
  };
}
