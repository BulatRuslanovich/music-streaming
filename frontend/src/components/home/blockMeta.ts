// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { Route } from "next";
import type { TranslationKey } from "@/lib/i18n";
import { reasonLabel } from "@/lib/recommendationReason";
import type { HomeBlock } from "@/lib/types";
import type { Translate } from "@/contexts/I18nContext";

const DAILY_MIX = "dailyMix";
const FAVORITES = "favorites";
const QUICK_TILES = "quickTiles";
const NEW_ARRIVALS = "newArrivals";
const TOP_TRACKS = "topTracks";

const TITLES: Record<string, TranslationKey> = {
  [DAILY_MIX]: "home.dailyMix",
  [FAVORITES]: "home.likedSongs",
  [QUICK_TILES]: "home.quickPicks",
  [NEW_ARRIVALS]: "home.newArrivals",
  [TOP_TRACKS]: "home.topThisWeek",
  newAlbums: "home.newAlbums",
  yourPlaylists: "home.yourPlaylists",

  forYou: "rec.shelf.forYou",
  becauseYouListened: "rec.shelf.becauseYouListened",
  discover: "rec.shelf.discover",
  artistsForYou: "rec.shelf.artistsForYou",
};

const LINKS = {
  [DAILY_MIX]: "/mixes/daily",
  [FAVORITES]: "/favorites",
  [QUICK_TILES]: "/recently-played",
  [NEW_ARRIVALS]: "/mixes/new",
  [TOP_TRACKS]: "/mixes/top",
  newAlbums: "/albums",
  yourPlaylists: "/playlists",

  artistsForYou: "/artists",
} as const;

/** Литералы из LINKS — так typedRoutes проверяет их так же, как href в разметке. */
type BlockLink = (typeof LINKS)[keyof typeof LINKS];

const NEEDS_SUBJECT = new Set(["becauseYouListened"]);

/**
 * Полки, у которых заголовок называет саму подборку, а не причину: «Made for you», «Artists for
 * you». Причина у них есть и она содержательная — над ними и стоит подпись.
 *
 * Остальные рекомендательные полки её не получают. У `becauseYouListened` заголовок уже целиком
 * состоит из причины с субъектом, а у `discover` заголовок и `reason.kind` — это один и тот же
 * факт, сказанный дважды.
 */
const EXPLAINED = new Set(["forYou", "artistsForYou"]);

export function blockHref(block: HomeBlock): Route<BlockLink> | undefined {
  return (LINKS as Record<string, BlockLink | undefined>)[block.baseKey];
}

export function blockTitle(
  block: HomeBlock,
  translate: (key: TranslationKey, values?: Record<string, string | number>) => string,
): string {
  const subject = block.reason?.subject ?? undefined;
  const key = TITLES[block.baseKey];

  const usable = key && (!NEEDS_SUBJECT.has(block.baseKey) || subject);
  if (!usable) return translate("rec.shelf.forYou");

  return translate(key, subject ? { subject } : undefined);
}

/**
 * Строка под заголовком полки — и только там, где она добавляет то, чего в заголовке нет.
 * `undefined` здесь такой же осмысленный ответ, как строка: пояснение под каждой секцией
 * превратило бы иерархию обратно в шум.
 */
export function blockNote(block: HomeBlock, translate: Translate): string | undefined {
  if (block.baseKey === TOP_TRACKS) return translate("home.topPeriod");

  const reason = block.reason;
  if (!reason || !EXPLAINED.has(block.baseKey)) return undefined;

  return reasonLabel(reason, translate);
}
