// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { keepPreviousData, queryOptions, type QueryClient } from "@tanstack/react-query";
import { api, type PageParams, type TrackSort } from "@/lib/api";
import type { HomeMixSlug, StatisticsPeriod } from "@/lib/types";

// Пока летит следующая страница или новый фильтр, на экране остаётся предыдущий список:
// без этого смена страницы и каждый дебаунс поиска роняли всю таблицу в скелетон.
const keepPrevious = { placeholderData: keepPreviousData } as const;

// То же самое для сущности с id в ключе, но previous держится только внутри одного id:
// иначе на переходе артист A → артист B успевал мелькнуть чужой контент. Тип аргумента
// нарочно структурный — Query<TData> не подставляется в PlaceholderDataFunction по вариантности.
function keepPreviousOf<TData>(id: string | null) {
  return (
    previous: TData | undefined,
    query: { queryKey: readonly unknown[] } | undefined,
  ): TData | undefined => (query?.queryKey[1] === id ? previous : undefined);
}

export const queries = {
  homeFeed: (sectionSize = 12) =>
    queryOptions({ queryKey: ["homeFeed", sectionSize], queryFn: () => api.homeFeed(sectionSize) }),

  homeMix: (kind: HomeMixSlug) =>
    queryOptions({ queryKey: ["homeMix", kind], queryFn: () => api.homeMix(kind) }),

  tracks: (params: PageParams & { sort?: TrackSort; q?: string }) =>
    queryOptions({
      queryKey: ["tracks", params],
      queryFn: () => api.tracks(params),
      ...keepPrevious,
    }),

  albums: (params: PageParams & { recentFirst?: boolean; q?: string }) =>
    queryOptions({
      queryKey: ["albums", params],
      queryFn: () => api.albums(params),
      ...keepPrevious,
    }),

  album: (id: string) => queryOptions({ queryKey: ["album", id], queryFn: () => api.album(id) }),

  artists: (params: PageParams & { q?: string }) =>
    queryOptions({
      queryKey: ["artists", params],
      queryFn: () => api.artists(params),
      ...keepPrevious,
    }),

  artist: (id: string, params: PageParams = {}) =>
    queryOptions({
      queryKey: ["artist", id, params],
      queryFn: () => api.artist(id, params),
      placeholderData: keepPreviousOf(id),
    }),

  genres: () => queryOptions({ queryKey: ["genres"], queryFn: () => api.genres() }),

  genreTracks: (id: string | null, params: PageParams) =>
    queryOptions({
      queryKey: ["genreTracks", id, params],
      queryFn: () => api.genreTracks(id!, params),
      enabled: id !== null,
      placeholderData: keepPreviousOf(id),
    }),

  search: (q: string, limit = 25) =>
    queryOptions({
      queryKey: ["search", q, limit],
      queryFn: () => api.search(q, limit),
      enabled: q.length > 0,
      ...keepPrevious,
    }),

  favorites: (params: PageParams) =>
    queryOptions({
      queryKey: ["favorites", params],
      queryFn: () => api.favorites(params),
      ...keepPrevious,
    }),

  playlists: () => queryOptions({ queryKey: ["playlists"], queryFn: () => api.playlists() }),

  publicPlaylists: () =>
    queryOptions({ queryKey: ["playlists", "public"], queryFn: () => api.publicPlaylists() }),

  playlist: (id: string) =>
    queryOptions({ queryKey: ["playlist", id], queryFn: () => api.playlist(id) }),

  recentlyPlayed: (params: PageParams) =>
    queryOptions({
      queryKey: ["history", "recent", params],
      queryFn: () => api.recentlyPlayed(params),
      ...keepPrevious,
    }),

  history: (params: PageParams) =>
    queryOptions({
      queryKey: ["history", "log", params],
      queryFn: () => api.history(params),
      ...keepPrevious,
    }),

  statistics: (period: StatisticsPeriod) =>
    queryOptions({
      queryKey: ["statistics", period],
      queryFn: () => api.statistics(period),
      ...keepPrevious,
    }),

  lastfmStatus: () => queryOptions({ queryKey: ["lastfm"], queryFn: () => api.lastfmStatus() }),

  adminUsers: (params: PageParams) =>
    queryOptions({
      queryKey: ["adminUsers", params],
      queryFn: () => api.adminUsers(params),
      ...keepPrevious,
    }),
};

/**
 * Что грузит страница сразу после открытия — по ней и греется кэш при наведении на пункт
 * меню. Параметры обязаны совпадать с первым рендером страницы, иначе прогрев уляжется в
 * соседний ключ и пропадёт впустую; поэтому карта живёт здесь, рядом с самими ключами.
 */
export const navigationPrefetch: Record<string, (client: QueryClient) => Promise<void>> = {
  "/": (client) => client.prefetchQuery(queries.homeFeed()),
  "/tracks": (client) =>
    client.prefetchQuery(queries.tracks({ page: 1, pageSize: 100, sort: "Title", q: undefined })),
  "/albums": (client) =>
    client.prefetchQuery(
      queries.albums({ page: 1, pageSize: 60, recentFirst: false, q: undefined }),
    ),
  "/artists": (client) =>
    client.prefetchQuery(queries.artists({ page: 1, pageSize: 60, q: undefined })),
  "/genres": (client) => client.prefetchQuery(queries.genres()),
  "/favorites": (client) => client.prefetchQuery(queries.favorites({ page: 1, pageSize: 100 })),
  "/recently-played": (client) =>
    client.prefetchQuery(queries.recentlyPlayed({ page: 1, pageSize: 100 })),
  "/playlists": (client) => client.prefetchQuery(queries.playlists()),
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
