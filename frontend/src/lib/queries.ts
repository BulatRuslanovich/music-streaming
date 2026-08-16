import { queryOptions } from "@tanstack/react-query";
import { api, type PageParams, type TrackSort } from "@/lib/api";
import type { StatisticsPeriod } from "@/lib/types";

/**
 * Ключи запросов в одном месте. Раньше их роль играла строка cacheKey, которую каждая
 * страница придумывала себе сама, — и совпадение ключа с тем, что реально запрашивается,
 * ничем не проверялось. Здесь ключ и загрузчик описаны рядом, поэтому разойтись не могут.
 *
 * Первый элемент ключа — область: по ней инвалидируется всё семейство сразу, когда
 * изменение затрагивает не один конкретный запрос (загрузили трек — устарели все списки).
 */
export const queries = {
  home: (sectionSize = 12) =>
    queryOptions({ queryKey: ["home", sectionSize], queryFn: () => api.home(sectionSize) }),

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

/**
 * Области, которые устаревают целиком. Точечная инвалидация здесь дороже, чем перезапрос.
 *
 * Главная (["home"]) попадает почти в каждую область не по невнимательности: она показывает
 * витрины сразу из всего — недавно добавленное, недавно сыгранное, избранное, плейлисты и
 * счётчики библиотеки. Пока её здесь не было, очистка истории обновляла страницу истории, а
 * витрину «Недавние» на главной оставляла с прежними треками до истечения staleTime.
 */
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
    ["recommendations"],
  ],
  playlists: [["playlists"], ["playlist"], ["home"]],
  // Отметка «избранное» едет внутри самих треков, в том числе на полках рекомендаций.
  favorites: [["favorites"], ["tracks"], ["home"], ["recommendations"]],
  history: [["history"], ["statistics"], ["home"]],
} as const;
