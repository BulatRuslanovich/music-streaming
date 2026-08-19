// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useSyncExternalStore } from "react";
import { PALETTES, THEME_COLORS, THEME_STORAGE_KEY, type Palette } from "./themeScript";

export type { Palette };
export { PALETTES };

// Хранится выбор пользователя, а применяется разрешённая палитра: `system` — это не тема, а
// указание спросить операционную систему.
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

  // Пока выбран `system`, за системной темой надо следить: её меняют на ходу, в том числе по
  // расписанию, и приложение должно идти следом без перезагрузки.
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

/** Палитра, которая сейчас на экране. Для `system` — уже разрешённая. */
export function useTheme(): Palette {
  return useSyncExternalStore(subscribe, getSnapshot, serverPalette);
}

/** Что выбрал пользователь, включая `system`. Нужно органам управления, а не оформлению. */
export function useThemeChoice(): ThemeChoice {
  return useSyncExternalStore(subscribe, getChoice, serverChoice);
}

/** Светлая ли сейчас палитра — для сторонних компонентов, знающих только про две темы. */
export function isLight(palette: Palette): boolean {
  return palette === "light";
}
