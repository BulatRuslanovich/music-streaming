// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { Album, Artist, LibraryStats, Playlist, Track } from "./catalog";
import type { RecommendationReason } from "./recommendations";

type HomeBlockLayout = "Shelf" | "Hero" | "Tile" | "QuickTiles" | "Grid" | "Chart" | "Circles";

type HomeZone = "Lead" | "Quick" | "Browse";

export interface HomeBlock {
  key: string;
  baseKey: string;
  layout: HomeBlockLayout;
  zone: HomeZone;
  reason?: RecommendationReason | null;
  tracks?: Track[] | null;
  albums?: Album[] | null;
  artists?: Artist[] | null;
  playlists?: Playlist[] | null;
  totalCount?: number | null;
}

export interface HomeFeed {
  blocks: HomeBlock[];
  stats: LibraryStats;
  isColdStart: boolean;
}

type HomeMixKind = "Daily" | "New" | "Top";

export type HomeMixSlug = "daily" | "new" | "top";

export interface HomeMix {
  kind: HomeMixKind;
  tracks: Track[];
}
