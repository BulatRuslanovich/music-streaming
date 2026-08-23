// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export const THEME_STORAGE_KEY = "music-streaming.theme";

export const PALETTES = ["dark", "midnight", "oled", "chameleon", "light", "paper"] as const;

export type Palette = (typeof PALETTES)[number];

// Совпадает с базовым `--canvas` каждой палитры: этим цветом браузер на телефоне красит свою
// строку вокруг страницы, и разъезжаться с фоном приложения ему нельзя. Тонировка обложкой сюда
// не заходит — она живёт только в CSS.
export const THEME_COLORS: Record<Palette, string> = {
  dark: "#0a0a09",
  midnight: "#06080f",
  oled: "#000000",
  // Хамелеон подмешивает в фон цвет обложки, но мета-тег анимировать нельзя — здесь чернильная
  // основа палитры, то есть её вид до того, как заиграл первый трек.
  chameleon: "#0b0b0e",
  light: "#f3f1ec",
  paper: "#efe9dc",
};

// Выполняется до первой отрисовки, поэтому вспышки чужой темы не бывает. Здесь же разрешается
// `system` — иначе системный выбор проступал бы только после гидратации.
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
