// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { TranslationKey } from "@/lib/i18n";
import type { HomeBlock } from "@/lib/types";
import type { PlaybackOrigin } from "@/contexts/PlayerContext";

export const DAILY_MIX = "dailyMix";
export const FAVORITES = "favorites";
export const QUICK_TILES = "quickTiles";
export const NEW_ARRIVALS = "newArrivals";
export const TOP_TRACKS = "topTracks";

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

const LINKS: Record<string, string> = {
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
};

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

export function isRecommendation(block: HomeBlock): boolean {
  return RECOMMENDATIONS.has(block.baseKey);
}

export function blockHref(block: HomeBlock): string | undefined {
  return LINKS[block.baseKey];
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
