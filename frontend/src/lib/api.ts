// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { flushEvents } from "@/lib/events";
import { fileForm, qs, request, requestFile } from "@/lib/http";
import { markImageChanged } from "@/lib/media";
import type {
  AdminUser,
  Album,
  AlbumDetail,
  Artist,
  ArtistDetail,
  BulkDeleteResult,
  ClientConfig,
  Genre,
  HistoryEntry,
  HomeFeed,
  HomeMix,
  HomeMixSlug,
  LibraryOverview,
  Lyrics,
  PageParams,
  Paged,
  PlaybackHandoff,
  PlaybackStateReport,
  PlayingElsewhere,
  Playlist,
  PlaylistDetail,
  RadioBatch,
  SearchResults,
  Track,
  TrackSort,
  UploadProbeFile,
  UploadProbeResult,
  User,
  UserSettings,
} from "@/lib/types";

const SECTION_SIZE = 12;
const RADIO_FLUSH_WAIT_MS = 1500;

export interface SearchTabResult {
  tracks: Paged<Track>;
  albums: Paged<Album>;
  artists: Paged<Artist>;
  genres: Paged<Genre>;
}

export type SearchTab = keyof SearchTabResult;

async function withImage<T>(upload: Promise<T>, kind: "album" | "artist" | "playlist", id: string) {
  const result = await upload;
  markImageChanged(kind, id, true);
  return result;
}

async function withoutImage(
  remove: Promise<void>,
  kind: "album" | "artist" | "playlist",
  id: string,
) {
  await remove;
  markImageChanged(kind, id, false);
}

export const api = {
  login: (username: string, password: string) =>
    request<User>("/auth/login", { method: "POST", body: { username, password } }),
  logout: () => request<void>("/auth/logout", { method: "POST" }),
  me: () => request<User>("/auth/me", { allowUnauthenticated: true }),
  config: () => request<ClientConfig>("/config"),
  settings: () => request<UserSettings>("/me/settings"),
  updateSettings: (changes: Partial<UserSettings>) =>
    request<UserSettings>("/me/settings", { method: "PUT", body: changes }),
  changePassword: (currentPassword: string, newPassword: string) =>
    request<void>("/me/password", { method: "POST", body: { currentPassword, newPassword } }),

  homeFeed: (signal?: AbortSignal) =>
    request<HomeFeed>(`/home/feed${qs({ sectionSize: SECTION_SIZE })}`, { signal }),
  homeMix: (kind: HomeMixSlug, signal?: AbortSignal) =>
    request<HomeMix>(`/home/mixes/${kind}`, { signal }),
  libraryOverview: () =>
    request<LibraryOverview>(`/library/overview${qs({ sectionSize: SECTION_SIZE })}`),

  tracks: (params: PageParams & { sort?: TrackSort; q?: string }, signal?: AbortSignal) =>
    request<Paged<Track>>(`/tracks${qs(params)}`, { signal }),
  shuffleTracks: (params: { limit?: number; q?: string } = {}) =>
    request<Track[]>(`/tracks/shuffle${qs(params)}`),
  artists: (params: PageParams & { q?: string }, signal?: AbortSignal) =>
    request<Paged<Artist>>(`/artists${qs(params)}`, { signal }),
  artist: (id: string, params: PageParams, signal?: AbortSignal) =>
    request<ArtistDetail>(`/artists/${id}${qs(params)}`, { signal }),
  artistTopTracks: (id: string) =>
    request<Track[]>(`/artists/${id}/top-tracks${qs({ limit: 10 })}`),
  albums: (
    params: PageParams & { artistId?: string; recentFirst?: boolean; q?: string },
    signal?: AbortSignal,
  ) => request<Paged<Album>>(`/albums${qs(params)}`, { signal }),
  album: (id: string, signal?: AbortSignal) => request<AlbumDetail>(`/albums/${id}`, { signal }),
  genres: (signal?: AbortSignal) => request<Genre[]>("/genres", { signal }),
  genreTracks: (id: string, params: PageParams, signal?: AbortSignal) =>
    request<Paged<Track>>(`/genres/${id}/tracks${qs(params)}`, { signal }),
  search: (q: string, limit: number, signal?: AbortSignal) =>
    request<SearchResults>(`/search${qs({ q, limit })}`, { signal }),
  searchTab: <T extends SearchTab>(tab: T, q: string, params: PageParams, signal?: AbortSignal) =>
    request<SearchTabResult[T]>(`/search/${tab}${qs({ q, ...params })}`, { signal }),
  lyrics: (trackId: string) => request<Lyrics | null>(`/tracks/${trackId}/lyrics`),

  updateTrack: (
    id: string,
    changes: {
      title?: string;
      artist?: string;
      album?: string;
      genre?: string;
      year?: number | null;
      trackNumber?: number | null;
      discNumber?: number | null;
    },
  ) => request<Track>(`/tracks/${id}`, { method: "PUT", body: changes }),
  updateLyrics: (trackId: string, text: string) =>
    request<Lyrics | null>(`/tracks/${trackId}/lyrics`, { method: "PUT", body: { text } }),
  deleteTrack: (id: string) => request<void>(`/tracks/${id}`, { method: "DELETE" }),
  deleteTracks: (ids: string[]) =>
    request<BulkDeleteResult>("/tracks/bulk-delete", { method: "POST", body: { ids } }),
  downloadTrack: (id: string, fallbackName: string) =>
    requestFile(`/tracks/${id}/download`, fallbackName),
  checkUpload: (files: UploadProbeFile[]) =>
    request<UploadProbeResult>("/tracks/upload/check", { method: "POST", body: { files } }),
  updateArtist: (id: string, name: string) =>
    request<Artist>(`/artists/${id}`, { method: "PUT", body: { name } }),
  uploadArtistImage: (id: string, file: File) =>
    withImage(
      request<Artist>(`/artists/${id}/image`, { method: "POST", body: fileForm(file) }),
      "artist",
      id,
    ),
  removeArtistImage: (id: string) =>
    withoutImage(request<void>(`/artists/${id}/image`, { method: "DELETE" }), "artist", id),
  updateAlbum: (id: string, changes: { title?: string; artist?: string; year?: number | null }) =>
    request<Album>(`/albums/${id}`, { method: "PUT", body: changes }),
  uploadAlbumCover: (id: string, file: File) =>
    withImage(
      request<Album>(`/albums/${id}/cover`, { method: "POST", body: fileForm(file) }),
      "album",
      id,
    ),
  removeAlbumCover: (id: string) =>
    withoutImage(request<void>(`/albums/${id}/cover`, { method: "DELETE" }), "album", id),

  favorites: (params: PageParams) => request<Paged<Track>>(`/favorites${qs(params)}`),
  addFavorite: (trackId: string) =>
    request<void>(`/tracks/${trackId}/favorite`, { method: "POST" }),
  removeFavorite: (trackId: string) =>
    request<void>(`/tracks/${trackId}/favorite`, { method: "DELETE" }),
  playlists: () => request<Playlist[]>("/playlists"),
  publicPlaylists: () => request<Playlist[]>("/playlists/public"),
  playlist: (id: string) => request<PlaylistDetail>(`/playlists/${id}`),
  createPlaylist: (name: string, description?: string, isPublic = false) =>
    request<Playlist>("/playlists", { method: "POST", body: { name, description, isPublic } }),
  updatePlaylist: (id: string, name: string, description?: string | null, isPublic = false) =>
    request<Playlist>(`/playlists/${id}`, {
      method: "PUT",
      body: { name, description, isPublic },
    }),
  deletePlaylist: (id: string) => request<void>(`/playlists/${id}`, { method: "DELETE" }),
  addToPlaylist: (playlistId: string, trackIds: string[]) =>
    request<void>(`/playlists/${playlistId}/tracks`, { method: "POST", body: { trackIds } }),
  removeFromPlaylist: (playlistId: string, trackId: string) =>
    request<void>(`/playlists/${playlistId}/tracks/${trackId}`, { method: "DELETE" }),
  reorderPlaylist: (playlistId: string, trackIds: string[]) =>
    request<void>(`/playlists/${playlistId}/tracks/order`, { method: "PUT", body: { trackIds } }),
  uploadPlaylistCover: (id: string, file: File) =>
    withImage(
      request<Playlist>(`/playlists/${id}/cover`, { method: "POST", body: fileForm(file) }),
      "playlist",
      id,
    ),
  removePlaylistCover: (id: string) =>
    withoutImage(request<void>(`/playlists/${id}/cover`, { method: "DELETE" }), "playlist", id),

  history: (params: PageParams) => request<Paged<HistoryEntry>>(`/history${qs(params)}`),
  recentlyPlayed: (params: PageParams) => request<Paged<Track>>(`/history/recent${qs(params)}`),
  recordPlay: (trackId: string, playbackPosition: number) =>
    request<void>("/history", { method: "POST", body: { trackId, playbackPosition } }),
  clearHistory: () => request<void>("/history", { method: "DELETE" }),
  reportPlayback: (report: PlaybackStateReport) =>
    request<void>("/playback/state", { method: "PUT", body: report }),
  playingElsewhere: (deviceId: string, signal?: AbortSignal) =>
    request<PlayingElsewhere | undefined>(`/playback/now${qs({ deviceId })}`, { signal }),
  handoff: (deviceId: string) =>
    request<PlaybackHandoff>("/playback/handoff", { method: "POST", body: { deviceId } }),
  // Радио подстраивается под скипы последних минут, поэтому сначала уходят накопленные события.
  radio: async (body: {
    seedTrackId: string | null;
    exclude: string[];
    limit?: number;
    mood?: string | null;
  }) => {
    await flushEvents(RADIO_FLUSH_WAIT_MS);
    return request<RadioBatch>("/recommendations/radio", { method: "POST", body });
  },
  moods: () => request<string[]>("/recommendations/moods"),

  adminUsers: (params: PageParams) => request<Paged<AdminUser>>(`/admin/users${qs(params)}`),
  createUser: (body: { username: string; password: string; isAdmin: boolean }) =>
    request<AdminUser>("/admin/users", { method: "POST", body }),
  setUserActive: (id: string, isActive: boolean) =>
    request<AdminUser>(`/admin/users/${id}/active`, { method: "PUT", body: { isActive } }),
  setUserRole: (id: string, isAdmin: boolean) =>
    request<AdminUser>(`/admin/users/${id}/role`, { method: "PUT", body: { isAdmin } }),
  resetUserPassword: (id: string, newPassword: string) =>
    request<void>(`/admin/users/${id}/password`, { method: "POST", body: { newPassword } }),
  revokeUserSessions: (id: string) =>
    request<void>(`/admin/users/${id}/sessions/revoke`, { method: "POST" }),
};
