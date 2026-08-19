// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useSyncExternalStore } from "react";
import { THEME_COLORS, THEME_STORAGE_KEY } from "./themeScript";

export type Theme = "dark" | "light";

let currentTheme: Theme | null = null;
const listeners = new Set<() => void>();

function readTheme(): Theme {
  try {
    const saved = window.localStorage.getItem(THEME_STORAGE_KEY);
    if (saved === "light") return "light";
  } catch {}
  return "dark";
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function getSnapshot(): Theme {
  if (currentTheme === null) currentTheme = readTheme();
  return currentTheme;
}

function getServerSnapshot(): Theme {
  return "dark";
}

export function setTheme(next: Theme): void {
  currentTheme = next;
  document.documentElement.dataset.theme = next;
  document.querySelector('meta[name="theme-color"]')?.setAttribute("content", THEME_COLORS[next]);
  try {
    window.localStorage.setItem(THEME_STORAGE_KEY, next);
  } catch {}
  listeners.forEach((listener) => listener());
}

export function useTheme(): Theme {
  return useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
}
