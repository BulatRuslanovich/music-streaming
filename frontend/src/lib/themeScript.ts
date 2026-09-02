// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export const THEME_STORAGE_KEY = "music-streaming.theme";

export const THEME_UNLOCK_KEY = "music-streaming.theme-unlocked";

/** Палитры, которые предлагаются сами: настройки, переключатель темы в палитре команд. */
export const PALETTES = ["dark", "light"] as const;

/** Палитры, которые нужно найти. В списки выбора попадают только после разблокировки. */
export const SECRET_PALETTES = ["jdm"] as const;

export const ALL_PALETTES = [...PALETTES, ...SECRET_PALETTES] as const;

export type PublicPalette = (typeof PALETTES)[number];

export type Palette = (typeof ALL_PALETTES)[number];

export const THEME_COLORS: Record<Palette, string> = {
  dark: "#000000",
  light: "#ededed",
  jdm: "#05070a",
};

// Скрытая палитра обязана быть здесь же: скрипт валидирует сохранённое значение по THEME_COLORS,
// и без неё разблокированная тема на каждой перезагрузке мигала бы обратно в тёмную.
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
