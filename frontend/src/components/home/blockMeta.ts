// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { Route } from "next";
import type { TranslationKey } from "@/lib/i18n";
import { reasonLabel } from "@/lib/recommendationReason";
import type { HomeBlock } from "@/lib/types";
import type { Translate } from "@/contexts/I18nContext";
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

const RECOMMENDATIONS = new Set(["forYou", "becauseYouListened", "discover", "artistsForYou"]);

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

export function blockHref(block: HomeBlock): Route<BlockLink> | undefined {
  return (LINKS as Record<string, BlockLink | undefined>)[block.baseKey];
}

export function blockOrigin(block: HomeBlock): PlaybackOrigin {
  if (isRecommendation(block)) {
    return { source: "recommendation" };
  }

  if (block.baseKey === FAVORITES) return { source: "favorites" };

  return { source: "home" };
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
 * Строка над заголовком полки — и только там, где она добавляет то, чего в заголовке нет.
 * `undefined` здесь такой же осмысленный ответ, как строка: пустой `Overline` над каждой
 * секцией превратил бы иерархию обратно в шум, ради которого всё и затевалось.
 */
export function blockEyebrow(block: HomeBlock, translate: Translate): string | undefined {
  if (block.baseKey === TOP_TRACKS) return translate("home.topPeriod");

  const reason = block.reason;
  if (!reason || !EXPLAINED.has(block.baseKey)) return undefined;

  return reasonLabel(reason, translate);
}
