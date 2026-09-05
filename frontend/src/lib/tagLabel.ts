// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

/**
 * Теги хранятся так, как их отдаёт Last.fm после нормализации, — целиком в нижнем регистре,
 * потому что имя тега там же и ключ. Человеку «post-punk» рядом с названиями треков читается
 * как опечатка, поэтому регистр восстанавливается на показе, а не в базе.
 */

/** Слова, которые в нижнем регистре выглядят ошибкой, а не стилем. */
const ACRONYMS = new Set([
  "r&b",
  "edm",
  "idm",
  "ebm",
  "ost",
  "uk",
  "us",
  "usa",
  "nyc",
  "dj",
  "mpb",
  "jpop",
  "kpop",
]);

/** Служебные слова внутри тега с большой буквы не пишутся: «drum and bass», не «Drum And Bass». */
const MINOR = new Set(["and", "or", "of", "the", "a", "an", "in", "on", "n"]);

export function tagLabel(name: string): string {
  const trimmed = name.trim();
  if (trimmed.length === 0) return "";

  return trimmed
    .split(" ")
    .filter((word) => word.length > 0)
    .map((word, index) => {
      const lower = word.toLowerCase();

      if (ACRONYMS.has(lower)) return lower.toUpperCase();
      if (index > 0 && MINOR.has(lower)) return lower;

      // Дефис остаётся внутри слова: «post-punk», а не «Post-Punk» — так пишет и сам жанр.
      return lower.charAt(0).toUpperCase() + lower.slice(1);
    })
    .join(" ");
}
