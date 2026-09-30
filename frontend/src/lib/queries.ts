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
import { api, type PageParams, type TrackSort } from "@/lib/api";
import { HOME_SECTION_SIZE } from "@/lib/api/contracts";
import type { Album, Artist, ArtistDetail, Genre, HomeMixSlug, Paged, Track } from "@/lib/types";

const keepPrevious = { placeholderData: keepPreviousData } as const;

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

interface SearchTabResult {
  tracks: Paged<Track>;
  albums: Paged<Album>;
  artists: Paged<Artist>;
  genres: Paged<Genre>;
}

export type SearchTab = keyof SearchTabResult;

const searchTabFetchers: {
  [T in SearchTab]: (
    q: string,
    params: PageParams,
    signal?: AbortSignal,
  ) => Promise<SearchTabResult[T]>;
} = {
  tracks: (q, params, signal) => api.searchTracks(q, params, signal),
  albums: (q, params, signal) => api.searchAlbums(q, params, signal),
  artists: (q, params, signal) => api.searchArtists(q, params, signal),
  genres: (q, params, signal) => api.searchGenres(q, params, signal),
};

/**
 * Короче сервер не ищет вовсе (`SearchTerm.MinimumLength` на бэкенде) и отвечает пустым
 * результатом. Держим то же число здесь: иначе запрос уходил впустую, а страница писала
 * «ничего не найдено» там, где поиск просто ещё не начался.
 */
export const SEARCH_MIN_LENGTH = 3;

export const queries = {
  homeFeed: (sectionSize: number = HOME_SECTION_SIZE) =>
    queryOptions({
      queryKey: ["homeFeed", sectionSize],
      queryFn: ({ signal }) => api.homeFeed(sectionSize, signal),
    }),

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
      ...keepPrevious,
    }),

  albums: (params: PageParams & { artistId?: string; recentFirst?: boolean; q?: string }) =>
    queryOptions({
      queryKey: ["albums", params],
      queryFn: ({ signal }) => api.albums(params, signal),
      ...keepPrevious,
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

  artistTopTracks: (id: string, limit = 10) =>
    queryOptions({
      queryKey: ["artist", id, "top", limit],
      queryFn: () => api.artistTopTracks(id, limit),
    }),

  genres: () => queryOptions({ queryKey: ["genres"], queryFn: ({ signal }) => api.genres(signal) }),

  genreTracks: (id: string | null, params: PageParams) =>
    queryOptions({
      queryKey: ["genreTracks", id, params],
      queryFn: ({ signal }) => api.genreTracks(id!, params, signal),
      enabled: id !== null,
      placeholderData: keepPreviousOf<Paged<Track>>(id),
    }),

  search: (q: string, limit = 25) =>
    queryOptions({
      queryKey: ["search", q, limit],
      queryFn: ({ signal }) => api.search(q, limit, signal),
      enabled: q.length >= SEARCH_MIN_LENGTH,
      ...keepPrevious,
    }),

  searchTab: <T extends SearchTab>(tab: T, q: string, params: PageParams) =>
    queryOptions({
      queryKey: ["search", tab, q, params],
      queryFn: ({ signal }): Promise<SearchTabResult[T]> =>
        searchTabFetchers[tab](q, params, signal),
      enabled: q.length >= SEARCH_MIN_LENGTH,
      ...keepPrevious,
    }),

  favorites: (params: PageParams) =>
    queryOptions({
      queryKey: ["favorites", params],
      queryFn: () => api.favorites(params),
      ...keepPrevious,
    }),

  playlists: () => queryOptions({ queryKey: ["playlists"], queryFn: () => api.playlists() }),

  libraryOverview: (sectionSize = 12) =>
    queryOptions({
      queryKey: ["libraryOverview", sectionSize],
      queryFn: () => api.libraryOverview(sectionSize),
    }),

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

  adminUsers: (params: PageParams) =>
    queryOptions({
      queryKey: ["adminUsers", params],
      queryFn: () => api.adminUsers(params),
      ...keepPrevious,
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
