// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { Route } from "next";
import type { TranslationKey } from "@/lib/i18n";
import type { HomeBlock, Track } from "@/lib/types";
import type { PlaybackOrigin } from "@/contexts/PlayerContext";

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

  continueListening: "rec.shelf.continueListening",
  forYou: "rec.shelf.forYou",
  similarTo: "rec.shelf.similarTo",
  becauseYouListened: "rec.shelf.becauseYouListened",
  discover: "rec.shelf.discover",
  genreMix: "rec.shelf.genreMix",
  morningMix: "rec.shelf.morningMix",
  dayMix: "rec.shelf.dayMix",
  eveningMix: "rec.shelf.eveningMix",
  nightMix: "rec.shelf.nightMix",
  newReleases: "rec.shelf.newReleases",
  popular: "rec.shelf.popular",
  artistsForYou: "rec.shelf.artistsForYou",
  albumsForYou: "rec.shelf.albumsForYou",
};

const LINKS = {
  [DAILY_MIX]: "/mixes/daily",
  [FAVORITES]: "/favorites",
  [QUICK_TILES]: "/recently-played",
  [NEW_ARRIVALS]: "/mixes/new",
  [TOP_TRACKS]: "/mixes/top",
  newAlbums: "/albums",
  yourPlaylists: "/playlists",

  continueListening: "/recently-played",
  newReleases: "/tracks",
  artistsForYou: "/artists",
  albumsForYou: "/albums",
} as const;

/** Литералы из LINKS — так typedRoutes проверяет их так же, как href в разметке. */
type BlockLink = (typeof LINKS)[keyof typeof LINKS];

const RECOMMENDATIONS = new Set([
  "continueListening",
  "forYou",
  "similarTo",
  "becauseYouListened",
  "discover",
  "genreMix",
  "morningMix",
  "dayMix",
  "eveningMix",
  "nightMix",
  "newReleases",
  "popular",
  "artistsForYou",
  "albumsForYou",
]);

const NEEDS_SUBJECT = new Set(["similarTo", "becauseYouListened", "genreMix"]);

/**
 * Хвост ленты на узком экране: блоки, содержимое которых и так лежит за отдельным пунктом
 * навигации (/albums, /artists, /playlists), плюс вторая рекомендательная полка — она того же
 * рода, что первая, тремя карточками ниже. На телефоне они уезжают под «Показать ещё».
 *
 * `artistsForYou` здесь ещё и потому, что это единственный блок с круглыми обложками: целая
 * визуальная грамматика ради двенадцати имён.
 */
const MOBILE_TAIL = new Set(["newAlbums", "artistsForYou", "yourPlaylists"]);

/** Начиная с какой по счёту рекомендательной полки они уходят в хвост. */
const TAIL_FROM_RECOMMENDATION = 1;

function isRecommendation(block: HomeBlock): boolean {
  return RECOMMENDATIONS.has(block.baseKey);
}

/**
 * Полка определяется порядковым номером среди рекомендательных, а не ключом: какой именно
 * `baseKey` окажется первым, решает ShelfPriority на бэкенде и вкус слушателя.
 */
function isMobileTail(block: HomeBlock, recommendationIndex: number): boolean {
  if (MOBILE_TAIL.has(block.baseKey)) return true;

  return isRecommendation(block) && recommendationIndex >= TAIL_FROM_RECOMMENDATION;
}

/**
 * Раскладывает блоки зоны Browse на голову и хвост, попутно считая рекомендательные полки.
 * `artistsForYou` из счёта исключён: он приезжает рекомендацией, но это не «ещё одна полка
 * для вас», а отдельный блок, и в хвост он попадает по имени.
 */
export function splitMobileTail(browse: HomeBlock[]): { head: HomeBlock[]; tail: HomeBlock[] } {
  const head: HomeBlock[] = [];
  const tail: HomeBlock[] = [];

  let shelves = 0;

  for (const block of browse) {
    const counts = isRecommendation(block) && !MOBILE_TAIL.has(block.baseKey);
    const index = counts ? shelves++ : -1;

    (isMobileTail(block, index) ? tail : head).push(block);
  }

  return { head, tail };
}

/**
 * Сколько разных обложек набрать для мозаик радио. `RadioRow.artworkFor` индексирует
 * `(modeIndex * 3 + offset) % tracks.length` при modeIndex ≤ 3 и offset ≤ 3, то есть дальше
 * двенадцатого индекса не заглядывает никогда — шестнадцати заведомо хватает на четыре
 * различимые мозаики.
 */
export const MOSAIC_POOL = 16;

/**
 * Первые {@link MOSAIC_POOL} различных треков ленты — сырьё для обложек радио-плиток.
 * Раньше на каждый рендер строилась карта на все ~130 треков ленты ради шестнадцати.
 */
export function mosaicPool(blocks: HomeBlock[]): Track[] {
  const seen = new Map<string, Track>();

  for (const block of blocks) {
    // Геро сюда не входит: его треки и так на виду прямо над плитками, и в мозаике под ними
    // выглядели бы повтором.
    if (block.zone === "Lead") continue;

    for (const track of block.tracks ?? []) {
      if (!seen.has(track.id)) seen.set(track.id, track);
      if (seen.size === MOSAIC_POOL) return [...seen.values()];
    }
  }

  return [...seen.values()];
}

export function blockHref(block: HomeBlock): Route<BlockLink> | undefined {
  return (LINKS as Record<string, BlockLink | undefined>)[block.baseKey];
}

export function blockOrigin(block: HomeBlock): PlaybackOrigin {
  if (isRecommendation(block)) {
    return { source: "recommendation", sourceId: block.reason?.subjectId ?? undefined };
  }

  if (block.baseKey === FAVORITES) return { source: "favorites" };

  return { source: "home", sourceId: block.baseKey };
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
