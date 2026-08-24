// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useSyncExternalStore } from "react";
import { PALETTES, THEME_COLORS, THEME_STORAGE_KEY, type Palette } from "./themeScript";

export type { Palette };
export { PALETTES };

export const THEME_CHOICES = ["system", ...PALETTES] as const;

export type ThemeChoice = (typeof THEME_CHOICES)[number];

const LIGHT_QUERY = "(prefers-color-scheme: light)";

let choice: ThemeChoice | null = null;

const listeners = new Set<() => void>();

function isChoice(value: string | null): value is ThemeChoice {
  return value !== null && (THEME_CHOICES as readonly string[]).includes(value);
}

function readChoice(): ThemeChoice {
  try {
    const saved = window.localStorage.getItem(THEME_STORAGE_KEY);
    if (isChoice(saved)) return saved;
  } catch {}

  return "dark";
}

function resolve(value: ThemeChoice): Palette {
  if (value !== "system") return value;

  return window.matchMedia(LIGHT_QUERY).matches ? "light" : "dark";
}

function apply(palette: Palette): void {
  document.documentElement.dataset.theme = palette;
  document
    .querySelector('meta[name="theme-color"]')
    ?.setAttribute("content", THEME_COLORS[palette]);
}

function notify(): void {
  listeners.forEach((listener) => listener());
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);

  const media = window.matchMedia(LIGHT_QUERY);
  const onSystemChange = () => {
    if (getChoice() !== "system") return;

    apply(resolve("system"));
    notify();
  };

  media.addEventListener("change", onSystemChange);

  return () => {
    listeners.delete(listener);
    media.removeEventListener("change", onSystemChange);
  };
}

function getChoice(): ThemeChoice {
  if (choice === null) choice = readChoice();
  return choice;
}

function getSnapshot(): Palette {
  return resolve(getChoice());
}

function serverChoice(): ThemeChoice {
  return "dark";
}

function serverPalette(): Palette {
  return "dark";
}

export function setTheme(next: ThemeChoice): void {
  choice = next;
  apply(resolve(next));

  try {
    window.localStorage.setItem(THEME_STORAGE_KEY, next);
  } catch {}

  notify();
}

export function useTheme(): Palette {
  return useSyncExternalStore(subscribe, getSnapshot, serverPalette);
}

export function useThemeChoice(): ThemeChoice {
  return useSyncExternalStore(subscribe, getChoice, serverChoice);
}

const LIGHT_PALETTES: ReadonlySet<Palette> = new Set<Palette>(["light", "paper"]);

export function isLight(palette: Palette): boolean {
  return LIGHT_PALETTES.has(palette);
}
