import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { applyStoredTheme } from "@/lib/theme";
import { mockColorScheme } from "@/test/match-media";

import { ThemeToggle } from "./theme-toggle";

describe("ThemeToggle", () => {
  it("follows the OS colour scheme when nothing is stored", () => {
    mockColorScheme(true);
    render(<ThemeToggle />);
    expect(screen.getByRole("button", { name: "Switch to light theme" })).toBeInTheDocument();
    expect(document.documentElement.dataset["theme"]).toBeUndefined();
  });

  it("updates live when the OS scheme changes", () => {
    const media = mockColorScheme(false);
    render(<ThemeToggle />);
    expect(screen.getByRole("button", { name: "Switch to dark theme" })).toBeInTheDocument();

    act(() => media.setDark(true));

    expect(screen.getByRole("button", { name: "Switch to light theme" })).toBeInTheDocument();
  });

  it("pins the chosen theme on <html> and remembers it", async () => {
    mockColorScheme(false);
    render(<ThemeToggle />);

    await userEvent.setup().click(screen.getByRole("button", { name: "Switch to dark theme" }));

    expect(document.documentElement.dataset["theme"]).toBe("dark");
    expect(window.localStorage.getItem("dp-theme")).toBe("dark");
    expect(screen.getByRole("button", { name: "Switch to light theme" })).toBeInTheDocument();

    await userEvent.setup().click(screen.getByRole("button", { name: "Switch to light theme" }));
    expect(document.documentElement.dataset["theme"]).toBe("light");
  });

  it("restores the stored choice on startup, even against the OS scheme", () => {
    mockColorScheme(true);
    window.localStorage.setItem("dp-theme", "light");

    applyStoredTheme();
    render(<ThemeToggle />);

    expect(document.documentElement.dataset["theme"]).toBe("light");
    expect(screen.getByRole("button", { name: "Switch to dark theme" })).toBeInTheDocument();
  });

  it("ignores a corrupted stored value", () => {
    mockColorScheme(false);
    window.localStorage.setItem("dp-theme", "purple");

    applyStoredTheme();
    render(<ThemeToggle />);

    expect(document.documentElement.dataset["theme"]).toBeUndefined();
    expect(screen.getByRole("button", { name: "Switch to dark theme" })).toBeInTheDocument();
  });

  it("still works when storage is blocked", async () => {
    mockColorScheme(false);
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new DOMException("denied", "SecurityError");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new DOMException("denied", "SecurityError");
    });
    render(<ThemeToggle />);

    await userEvent.setup().click(screen.getByRole("button", { name: "Switch to dark theme" }));

    expect(document.documentElement.dataset["theme"]).toBe("dark");
  });
});
