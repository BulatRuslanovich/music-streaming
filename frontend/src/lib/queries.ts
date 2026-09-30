// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import {
  infiniteQueryOptions,
  keepPreviousData,
  queryOptions,
  type PlaceholderDataFunction,
  type QueryClient,
} from "@tanstack/react-query";
import { CARD_PAGE_SIZE, TRACK_PAGE_SIZE } from "@/lib/pageSizes";
import { api, type SearchTab } from "@/lib/api";
import type { ArtistDetail, HomeMixSlug, PageParams, Paged, Track, TrackSort } from "@/lib/types";

/**
 * Держит прошлые данные, пока меняется сущность, а не параметры страницы.
 *
 * Тип аргумента указывается на месте вызова и выводом не обходится: TData не участвует ни в
 * одном параметре, так что вывести его можно только из контекста, а контекст здесь — разбор
 * перегрузок queryOptions. Стоило queryFn перестать быть `() => ...`, как TData схлопывался
 * в undefined и утаскивал за собой тип всего запроса.
 */
function keepPreviousOf<TData>(id: string | null): PlaceholderDataFunction<TData> {
  return (previous, query) => (query?.queryKey[1] === id ? previous : undefined);
}

/**
 * Короче сервер не ищет вовсе (`SearchTerm.MinimumLength` на бэкенде) и отвечает пустым
 * результатом. Держим то же число здесь: иначе запрос уходил впустую, а страница писала
 * «ничего не найдено» там, где поиск просто ещё не начался.
 */
export const SEARCH_MIN_LENGTH = 3;

export const queries = {
  homeFeed: () =>
    queryOptions({ queryKey: ["homeFeed"], queryFn: ({ signal }) => api.homeFeed(signal) }),

  lyrics: (trackId: string) =>
    queryOptions({
      queryKey: ["lyrics", trackId],
      // 204 без тела приходит как undefined, а его TanStack Query считает ошибкой.
      queryFn: async () => (await api.lyrics(trackId)) ?? null,
      staleTime: Infinity,
    }),

  homeMix: (kind: HomeMixSlug) =>
    queryOptions({
      queryKey: ["homeMix", kind],
      queryFn: ({ signal }) => api.homeMix(kind, signal),
    }),

  tracks: (params: PageParams & { sort?: TrackSort; q?: string }) =>
    queryOptions({
      queryKey: ["tracks", params],
      queryFn: ({ signal }) => api.tracks(params, signal),
      placeholderData: keepPreviousData,
    }),

  albums: (params: PageParams & { artistId?: string; recentFirst?: boolean; q?: string }) =>
    queryOptions({
      queryKey: ["albums", params],
      queryFn: ({ signal }) => api.albums(params, signal),
      placeholderData: keepPreviousData,
    }),

  album: (id: string) =>
    queryOptions({ queryKey: ["album", id], queryFn: ({ signal }) => api.album(id, signal) }),

  /**
   * Каталог листается вниз, а не постранично: на библиотеке в тысячу альбомов кнопки
   * «вперёд» — это два десятка нажатий, и просмотр глазами ими разрывается. Ключ намеренно
   * отличается от постраничного (`["albums", ...]`), чтобы кэши не смешивались, но обе
   * ветки одинаково сбрасываются по `invalidate("library")`.
   */
  albumsFeed: ({ recentFirst = false, q }: { recentFirst?: boolean; q?: string } = {}) =>
    infiniteQueryOptions({
      queryKey: ["albums", "feed", { recentFirst, q }],
      queryFn: ({ pageParam, signal }) =>
        api.albums({ page: pageParam, pageSize: CARD_PAGE_SIZE, recentFirst, q }, signal),
      initialPageParam: 1,
      getNextPageParam: (last) => (last.page < last.totalPages ? last.page + 1 : undefined),
    }),

  artistsFeed: ({ q }: { q?: string } = {}) =>
    infiniteQueryOptions({
      queryKey: ["artists", "feed", { q }],
      queryFn: ({ pageParam, signal }) =>
        api.artists({ page: pageParam, pageSize: CARD_PAGE_SIZE, q }, signal),
      initialPageParam: 1,
      getNextPageParam: (last) => (last.page < last.totalPages ? last.page + 1 : undefined),
    }),

  artist: (id: string, page = 1) =>
    queryOptions({
      queryKey: ["artist", id, page],
      queryFn: ({ signal }) => api.artist(id, { page, pageSize: TRACK_PAGE_SIZE }, signal),
      placeholderData: keepPreviousOf<ArtistDetail>(id),
    }),

  artistTopTracks: (id: string) =>
    queryOptions({ queryKey: ["artist", id, "top"], queryFn: () => api.artistTopTracks(id) }),

  genres: () => queryOptions({ queryKey: ["genres"], queryFn: ({ signal }) => api.genres(signal) }),

  genreTracks: (id: string | null, params: PageParams) =>
    queryOptions({
      queryKey: ["genreTracks", id, params],
      queryFn: ({ signal }) => api.genreTracks(id!, params, signal),
      enabled: id !== null,
      placeholderData: keepPreviousOf<Paged<Track>>(id),
    }),

  search: (q: string) =>
    queryOptions({
      queryKey: ["search", q],
      queryFn: ({ signal }) => api.search(q, 25, signal),
      enabled: q.length >= SEARCH_MIN_LENGTH,
      placeholderData: keepPreviousData,
    }),

  searchTab: <T extends SearchTab>(tab: T, q: string, params: PageParams) =>
    queryOptions({
      queryKey: ["search", tab, q, params],
      queryFn: ({ signal }) => api.searchTab(tab, q, params, signal),
      enabled: q.length >= SEARCH_MIN_LENGTH,
      placeholderData: keepPreviousData,
    }),

  favorites: (params: PageParams) =>
    queryOptions({
      queryKey: ["favorites", params],
      queryFn: () => api.favorites(params),
      placeholderData: keepPreviousData,
    }),

  playlists: () => queryOptions({ queryKey: ["playlists"], queryFn: () => api.playlists() }),

  libraryOverview: () =>
    queryOptions({ queryKey: ["libraryOverview"], queryFn: () => api.libraryOverview() }),

  publicPlaylists: () =>
    queryOptions({ queryKey: ["playlists", "public"], queryFn: () => api.publicPlaylists() }),

  playlist: (id: string) =>
    queryOptions({ queryKey: ["playlist", id], queryFn: () => api.playlist(id) }),

  recentlyPlayed: (params: PageParams) =>
    queryOptions({
      queryKey: ["history", "recent", params],
      queryFn: () => api.recentlyPlayed(params),
      placeholderData: keepPreviousData,
    }),

  history: (params: PageParams) =>
    queryOptions({
      queryKey: ["history", "log", params],
      queryFn: () => api.history(params),
      placeholderData: keepPreviousData,
    }),

  adminUsers: (params: PageParams) =>
    queryOptions({
      queryKey: ["adminUsers", params],
      queryFn: () => api.adminUsers(params),
      placeholderData: keepPreviousData,
    }),
};

export const navigationPrefetch: Record<string, (client: QueryClient) => Promise<void>> = {
  "/": (client) => client.prefetchQuery(queries.homeFeed()),
  "/tracks": (client) =>
    client.prefetchQuery(
      queries.tracks({ page: 1, pageSize: TRACK_PAGE_SIZE, sort: "Title", q: undefined }),
    ),
  "/albums": (client) => client.prefetchInfiniteQuery(queries.albumsFeed()),
  "/artists": (client) => client.prefetchInfiniteQuery(queries.artistsFeed()),
  "/genres": (client) => client.prefetchQuery(queries.genres()),
  "/favorites": (client) =>
    client.prefetchQuery(queries.favorites({ page: 1, pageSize: TRACK_PAGE_SIZE })),
  "/recently-played": (client) =>
    client.prefetchQuery(queries.recentlyPlayed({ page: 1, pageSize: TRACK_PAGE_SIZE })),
  // Страница плейлистов теперь начинается с трёх карточек фонотеки, а они живут на обзоре.
  // Без его прогрева карточки приезжают позже настоящих плейлистов и сдвигают их вправо.
  "/playlists": async (client) => {
    await Promise.all([
      client.prefetchQuery(queries.playlists()),
      client.prefetchQuery(queries.libraryOverview()),
    ]);
  },
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
    ["homeFeed"],
    ["homeMix"],
    ["libraryOverview"],
  ],
  playlists: [["playlists"], ["playlist"], ["homeFeed"]],
  favorites: [["favorites"], ["tracks"], ["homeFeed"], ["homeMix"], ["libraryOverview"]],
  history: [["history"], ["homeFeed"], ["homeMix"]],
} as const;
