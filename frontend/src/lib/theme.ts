// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useSyncExternalStore } from "react";
import {
  ALL_PALETTES,
  PALETTES,
  SECRET_PALETTES,
  THEME_COLORS,
  THEME_STORAGE_KEY,
  THEME_UNLOCK_KEY,
  type Palette,
  type PublicPalette,
} from "./themeScript";

export { PALETTES };

const THEME_CHOICES = ["system", ...PALETTES] as const;

const ALL_THEME_CHOICES = ["system", ...ALL_PALETTES] as const;

export type ThemeChoice = (typeof ALL_THEME_CHOICES)[number];

const LIGHT_QUERY = "(prefers-color-scheme: light)";

let choice: ThemeChoice | null = null;

const listeners = new Set<() => void>();

function isChoice(value: string | null): value is ThemeChoice {
  return value !== null && (ALL_THEME_CHOICES as readonly string[]).includes(value);
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

export function isLight(palette: Palette): boolean {
  return palette === "light";
}

/**
 * Следующая палитра в открытом цикле. Скрытые в него не входят: иначе переключатель темы
 * рано или поздно наткнулся бы на них сам и находка перестала бы быть находкой. Из скрытой
 * палитры цикл возвращает в тёмную — `indexOf` не находит её и даёт первый элемент.
 */
export function nextPalette(current: Palette): PublicPalette {
  const index = (PALETTES as readonly string[]).indexOf(current);
  return PALETTES[(index + 1) % PALETTES.length];
}

let unlocked: boolean | null = null;

function isUnlocked(): boolean {
  if (unlocked !== null) return unlocked;

  try {
    unlocked = window.localStorage.getItem(THEME_UNLOCK_KEY) !== null;
  } catch {
    unlocked = false;
  }

  return unlocked;
}

// Ссылки постоянные: useSyncExternalStore сравнивает снимки по идентичности.
const PUBLIC_CHOICES: readonly ThemeChoice[] = THEME_CHOICES;
const UNLOCKED_CHOICES: readonly ThemeChoice[] = ALL_THEME_CHOICES;

function getChoices(): readonly ThemeChoice[] {
  return isUnlocked() ? UNLOCKED_CHOICES : PUBLIC_CHOICES;
}

function serverChoices(): readonly ThemeChoice[] {
  return PUBLIC_CHOICES;
}

/** Список тем для настроек: найденные палитры остаются в нём навсегда. */
export function useThemeChoices(): readonly ThemeChoice[] {
  return useSyncExternalStore(subscribe, getChoices, serverChoices);
}

/** Открывает скрытые палитры и сразу включает первую — иначе непонятно, что вообще случилось. */
export function unlockSecretPalettes(): Palette {
  unlocked = true;

  try {
    window.localStorage.setItem(THEME_UNLOCK_KEY, new Date().toISOString());
  } catch {}

  const [first] = SECRET_PALETTES;
  setTheme(first);

  return first;
}
