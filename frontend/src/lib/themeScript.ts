// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export const THEME_STORAGE_KEY = "music-streaming.theme";

export const PALETTES = ["dark", "midnight", "oled", "chameleon", "light", "paper"] as const;

export type Palette = (typeof PALETTES)[number];

export const THEME_COLORS: Record<Palette, string> = {
  dark: "#0a0a09",
  midnight: "#06080f",
  oled: "#000000",
  chameleon: "#0b0b0e",
  light: "#f3f1ec",
  paper: "#efe9dc",
};

export const NO_FLASH_THEME_SCRIPT = `try {
  var colors = ${JSON.stringify(THEME_COLORS)};
  var saved = localStorage.getItem("${THEME_STORAGE_KEY}");
  var palette = saved === "system"
    ? (matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark")
    : saved;
  if (!colors[palette]) palette = "dark";
  document.documentElement.dataset.theme = palette;
  var meta = document.querySelector('meta[name="theme-color"]');
  if (meta) meta.setAttribute("content", colors[palette]);
} catch (e) {}`;
