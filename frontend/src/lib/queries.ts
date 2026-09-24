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
import type { AdminListenerParams, AdminUploadParams } from "@/lib/api/adminStatistics";
import { HOME_SECTION_SIZE } from "@/lib/api/contracts";
import type {
  Album,
  AdminListenerDetail,
  Artist,
  ArtistDetail,
  Genre,
  HomeMixSlug,
  Paged,
  StatisticsPeriod,
  Track,
} from "@/lib/types";

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

export const queries = {
  // Громкость трека считает фоновый воркер: первый запрос по неизмеренному треку честно отвечает
  // `available: false` и ставит замер в очередь. Без переспроса нормализация не применилась бы к
  // первому прослушиванию вовсе — staleTime в пять минут длиннее самого трека. Переспрашиваем,
  // пока ответ отрицательный, и замолкаем, как только замер приехал.
  normalization: (id: string, mode: string) =>
    queryOptions({
      queryKey: ["normalization", id, mode],
      queryFn: ({ signal }) => api.normalization(id, mode, signal),
      enabled: !!id && mode !== "off",
      staleTime: 5 * 60_000,
      retry: false,
      refetchInterval: (query) => (query.state.data?.available === false ? 15_000 : false),
    }),
  // Итоги за закрытый месяц уже не изменятся, а окно живёт неделю — перепроверять нечего.
  // По той же причине recap не появляется в `invalidates`: новое прослушивание идёт в текущий
  // месяц, а показываем мы прошлый.
  monthlyRecap: () =>
    queryOptions({
      queryKey: ["recap"],
      queryFn: () => api.monthlyRecap(),
      staleTime: 60 * 60_000,
      retry: false,
    }),
  homeFeed: (sectionSize: number = HOME_SECTION_SIZE) =>
    queryOptions({
      queryKey: ["homeFeed", sectionSize],
      queryFn: ({ signal }) => api.homeFeed(sectionSize, signal),
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

  /**
   * Разбор записи не меняется, пока не сменится версия алгоритма анализа, — а она меняется
   * только вместе с перезапуском бэкенда. Перепроверять его в рамках сессии незачем.
   */
  trackAnalysis: (id: string) =>
    queryOptions({
      queryKey: ["trackAnalysis", id],
      queryFn: () => api.trackAnalysis(id),
      staleTime: Infinity,
      retry: false,
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
  albumsFeed: (params: { pageSize: number; recentFirst?: boolean; q?: string }) =>
    infiniteQueryOptions({
      queryKey: ["albums", "feed", params],
      queryFn: ({ pageParam, signal }) => api.albums({ ...params, page: pageParam }, signal),
      initialPageParam: 1,
      getNextPageParam: (last) => (last.page < last.totalPages ? last.page + 1 : undefined),
    }),

  artistsFeed: (params: { pageSize: number; q?: string }) =>
    infiniteQueryOptions({
      queryKey: ["artists", "feed", params],
      queryFn: ({ pageParam, signal }) => api.artists({ ...params, page: pageParam }, signal),
      initialPageParam: 1,
      getNextPageParam: (last) => (last.page < last.totalPages ? last.page + 1 : undefined),
    }),

  artist: (id: string, params: PageParams = {}) =>
    queryOptions({
      queryKey: ["artist", id, params],
      queryFn: ({ signal }) => api.artist(id, params, signal),
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

  // Теги меняются только когда воркер обогащения дотянет очередную порцию — раз в час.
  tags: () =>
    queryOptions({ queryKey: ["tags"], queryFn: () => api.tags(), staleTime: 30 * 60_000 }),

  tagTracks: (name: string | null, params: PageParams) =>
    queryOptions({
      queryKey: ["tagTracks", name, params],
      queryFn: () => api.tagTracks(name!, params),
      enabled: name !== null,
      placeholderData: keepPreviousOf<Paged<Track>>(name),
    }),

  tagArtists: (name: string | null) =>
    queryOptions({
      queryKey: ["tagArtists", name],
      queryFn: () => api.tagArtists(name!),
      enabled: name !== null,
    }),

  trackTags: (id: string) =>
    queryOptions({
      queryKey: ["trackTags", id],
      queryFn: () => api.trackTags(id),
      staleTime: 30 * 60_000,
    }),

  search: (q: string, limit = 25) =>
    queryOptions({
      queryKey: ["search", q, limit],
      queryFn: ({ signal }) => api.search(q, limit, signal),
      enabled: q.length > 0,
      ...keepPrevious,
    }),

  searchTab: <T extends SearchTab>(tab: T, q: string, params: PageParams) =>
    queryOptions({
      queryKey: ["search", tab, q, params],
      queryFn: ({ signal }): Promise<SearchTabResult[T]> =>
        searchTabFetchers[tab](q, params, signal),
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

  statistics: (period: StatisticsPeriod) =>
    queryOptions({
      queryKey: ["statistics", period],
      queryFn: () => api.statistics(period),
      ...keepPrevious,
    }),

  lastfmStatus: () => queryOptions({ queryKey: ["lastfm"], queryFn: () => api.lastfmStatus() }),

  libraryImport: () =>
    queryOptions({
      queryKey: ["libraryImport"],
      queryFn: () => api.importStatus(),
    }),

  adminUsers: (params: PageParams) =>
    queryOptions({
      queryKey: ["adminUsers", params],
      queryFn: () => api.adminUsers(params),
      ...keepPrevious,
    }),

  adminOverview: (period: StatisticsPeriod) =>
    queryOptions({
      queryKey: ["adminOverview", period],
      queryFn: () => api.adminOverview(period),
      ...keepPrevious,
    }),

  // Состояние каталога не зависит от периода и живёт своим ключом: обзор перезапрашивается на
  // каждое переключение периода, а этот запрос — самый тяжёлый из всех и переспрашивать его
  // незачем.
  adminCatalogHealth: () =>
    queryOptions({
      queryKey: ["adminCatalogHealth"],
      queryFn: () => api.adminCatalogHealth(),
    }),

  adminListeners: (params: AdminListenerParams) =>
    queryOptions({
      queryKey: ["adminListeners", params],
      queryFn: () => api.adminListeners(params),
      ...keepPrevious,
    }),

  adminListener: (id: string, period: StatisticsPeriod) =>
    queryOptions({
      queryKey: ["adminListener", id, period],
      queryFn: () => api.adminListener(id, period),
      placeholderData: keepPreviousOf<AdminListenerDetail>(id),
    }),

  adminUploads: (params: AdminUploadParams) =>
    queryOptions({
      queryKey: ["adminUploads", params],
      queryFn: () => api.adminUploads(params),
      ...keepPrevious,
    }),
};

export const navigationPrefetch: Record<string, (client: QueryClient) => Promise<void>> = {
  "/": (client) => client.prefetchQuery(queries.homeFeed()),
  "/tracks": (client) =>
    client.prefetchQuery(
      queries.tracks({ page: 1, pageSize: TRACK_PAGE_SIZE, sort: "Title", q: undefined }),
    ),
  "/albums": (client) =>
    client.prefetchInfiniteQuery(
      queries.albumsFeed({ pageSize: CARD_PAGE_SIZE, recentFirst: false, q: undefined }),
    ),
  "/artists": (client) =>
    client.prefetchInfiniteQuery(queries.artistsFeed({ pageSize: CARD_PAGE_SIZE, q: undefined })),
  "/genres": (client) => client.prefetchQuery(queries.genres()),
  "/tags": (client) => client.prefetchQuery(queries.tags()),
  "/recap": (client) => client.prefetchQuery(queries.monthlyRecap()),
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
    ["home"],
    ["homeFeed"],
    ["homeMix"],
    ["libraryOverview"],
  ],
  playlists: [["playlists"], ["playlist"], ["home"], ["homeFeed"]],
  favorites: [["favorites"], ["tracks"], ["home"], ["homeFeed"], ["homeMix"], ["libraryOverview"]],
  history: [["history"], ["statistics"], ["home"], ["homeFeed"], ["homeMix"]],
  recommendations: [["home"], ["homeFeed"], ["homeMix"]],
} as const;
