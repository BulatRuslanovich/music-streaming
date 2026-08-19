// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { queryOptions } from "@tanstack/react-query";
import { api, type PageParams, type TrackSort } from "@/lib/api";
import type { HomeMixSlug, StatisticsPeriod } from "@/lib/types";

export const queries = {
  home: (sectionSize = 12) =>
    queryOptions({ queryKey: ["home", sectionSize], queryFn: () => api.home(sectionSize) }),

  homeFeed: (sectionSize = 12) =>
    queryOptions({ queryKey: ["homeFeed", sectionSize], queryFn: () => api.homeFeed(sectionSize) }),

  homeMix: (kind: HomeMixSlug) =>
    queryOptions({ queryKey: ["homeMix", kind], queryFn: () => api.homeMix(kind) }),

  recommendations: (sectionSize = 12) =>
    queryOptions({
      queryKey: ["recommendations", sectionSize],
      queryFn: () => api.recommendations(sectionSize),
    }),

  tracks: (params: PageParams & { sort?: TrackSort; q?: string }) =>
    queryOptions({ queryKey: ["tracks", params], queryFn: () => api.tracks(params) }),

  albums: (params: PageParams & { recentFirst?: boolean; q?: string }) =>
    queryOptions({ queryKey: ["albums", params], queryFn: () => api.albums(params) }),

  album: (id: string) => queryOptions({ queryKey: ["album", id], queryFn: () => api.album(id) }),

  artists: (params: PageParams & { q?: string }) =>
    queryOptions({ queryKey: ["artists", params], queryFn: () => api.artists(params) }),

  artist: (id: string, params: PageParams = {}) =>
    queryOptions({ queryKey: ["artist", id, params], queryFn: () => api.artist(id, params) }),

  genres: () => queryOptions({ queryKey: ["genres"], queryFn: () => api.genres() }),

  genreTracks: (id: string | null, params: PageParams) =>
    queryOptions({
      queryKey: ["genreTracks", id, params],
      queryFn: () => api.genreTracks(id!, params),
      enabled: id !== null,
    }),

  search: (q: string, limit = 25) =>
    queryOptions({
      queryKey: ["search", q, limit],
      queryFn: () => api.search(q, limit),
      enabled: q.length > 0,
    }),

  favorites: (params: PageParams) =>
    queryOptions({ queryKey: ["favorites", params], queryFn: () => api.favorites(params) }),

  playlists: () => queryOptions({ queryKey: ["playlists"], queryFn: () => api.playlists() }),

  publicPlaylists: () =>
    queryOptions({ queryKey: ["playlists", "public"], queryFn: () => api.publicPlaylists() }),

  playlist: (id: string) =>
    queryOptions({ queryKey: ["playlist", id], queryFn: () => api.playlist(id) }),

  recentlyPlayed: (params: PageParams) =>
    queryOptions({
      queryKey: ["history", "recent", params],
      queryFn: () => api.recentlyPlayed(params),
    }),

  history: (params: PageParams) =>
    queryOptions({ queryKey: ["history", "log", params], queryFn: () => api.history(params) }),

  statistics: (period: StatisticsPeriod) =>
    queryOptions({ queryKey: ["statistics", period], queryFn: () => api.statistics(period) }),

  lastfmStatus: () => queryOptions({ queryKey: ["lastfm"], queryFn: () => api.lastfmStatus() }),

  adminUsers: (params: PageParams) =>
    queryOptions({ queryKey: ["adminUsers", params], queryFn: () => api.adminUsers(params) }),
};

export const invalidates = {
  library: [
    ["tracks"],
    ["albums"],
    ["album"],
    ["artists"],
    ["artist"],
    ["genres"],
    ["search"],
    ["home"],
    ["homeFeed"],
    ["homeMix"],
    ["recommendations"],
  ],
  playlists: [["playlists"], ["playlist"], ["home"], ["homeFeed"]],
  favorites: [["favorites"], ["tracks"], ["home"], ["homeFeed"], ["homeMix"], ["recommendations"]],
  history: [["history"], ["statistics"], ["home"], ["homeFeed"], ["homeMix"]],
} as const;
