import { tr } from "@/lib/i18n";
import { API_BASE, ApiError, GATEWAY_STATUSES, request, requestFile, query } from "@/lib/http";
import { markArtistImageChanged, markPlaylistCoverChanged } from "@/lib/media";


import type {
  AdminUser,
  Album,
  AlbumDetail,
  Artist,
  ArtistDetail,
  ClientConfig,
  Genre,
  HistoryEntry,
  HomeSummary,
  Paged,
  Playlist,
  PlaylistDetail,
  RecommendationHome,
  RecommendedTrack,
  SearchResults,
  Track,
  UploadResult,
  User,
} from "./types";

export type TrackSort = "Title" | "Recent" | "Artist" | "Album";

export interface PageParams {
  page?: number;
  pageSize?: number;
}

export interface UploadProgress {
  percent: number;
  fileIndex: number;
  fileCount: number;
  fileName: string;
}

export const api = {
  login: (username: string, password: string) =>
    request<User>("/auth/login", { method: "POST", body: { username, password } }),

  logout: () => request<void>("/auth/logout", { method: "POST" }),

  me: () => request<User>("/auth/me", { allowUnauthenticated: true }),

  config: () => request<ClientConfig>("/config"),

  home: (sectionSize = 12) => request<HomeSummary>(`/home${query({ sectionSize })}`),

  tracks: (params: PageParams & { sort?: TrackSort; q?: string } = {}) =>
    request<Paged<Track>>(`/tracks${query({ ...params })}`),

  track: (id: string) => request<Track>(`/tracks/${id}`),

  artists: (params: PageParams & { q?: string } = {}) =>
    request<Paged<Artist>>(`/artists${query({ ...params })}`),

  artist: (id: string, params: PageParams = {}) =>
    request<ArtistDetail>(`/artists/${id}${query({ ...params })}`),

  albums: (params: PageParams & { artistId?: string; recentFirst?: boolean; q?: string } = {}) =>
    request<Paged<Album>>(`/albums${query({ ...params })}`),

  album: (id: string) => request<AlbumDetail>(`/albums/${id}`),

  genres: () => request<Genre[]>("/genres"),

  genreTracks: (id: string, params: PageParams = {}) =>
    request<Paged<Track>>(`/genres/${id}/tracks${query({ ...params })}`),

  search: (q: string, limit = 20) => request<SearchResults>(`/search${query({ q, limit })}`),

  upload: (files: File[], onProgress?: (progress: UploadProgress) => void) =>
    uploadWithProgress(files, onProgress),

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

  deleteTrack: (id: string) => request<void>(`/tracks/${id}`, { method: "DELETE" }),

  downloadTrack: (id: string, fallbackName: string) =>
    requestFile(`/tracks/${id}/download`, fallbackName),

  favorites: (params: PageParams = {}) => request<Paged<Track>>(`/favorites${query({ ...params })}`),

  addFavorite: (trackId: string) => request<void>(`/tracks/${trackId}/favorite`, { method: "POST" }),

  removeFavorite: (trackId: string) =>
    request<void>(`/tracks/${trackId}/favorite`, { method: "DELETE" }),

  playlists: () => request<Playlist[]>("/playlists"),

  playlist: (id: string) => request<PlaylistDetail>(`/playlists/${id}`),

  createPlaylist: (name: string, description?: string) =>
    request<Playlist>("/playlists", { method: "POST", body: { name, description } }),

  updatePlaylist: (id: string, name: string, description?: string | null) =>
    request<Playlist>(`/playlists/${id}`, { method: "PUT", body: { name, description } }),

  deletePlaylist: (id: string) => request<void>(`/playlists/${id}`, { method: "DELETE" }),

  addToPlaylist: (playlistId: string, trackId: string) =>
    request<void>(`/playlists/${playlistId}/tracks`, { method: "POST", body: { trackId } }),

  removeFromPlaylist: (playlistId: string, trackId: string) =>
    request<void>(`/playlists/${playlistId}/tracks/${trackId}`, { method: "DELETE" }),

  reorderPlaylist: (playlistId: string, trackIds: string[]) =>
    request<void>(`/playlists/${playlistId}/tracks/order`, { method: "PUT", body: { trackIds } }),

  uploadPlaylistCover: async (id: string, file: File) => {
    const form = new FormData();
    form.append("file", file);

    const playlist = await request<Playlist>(`/playlists/${id}/cover`, {
      method: "POST",
      body: form,
    });
    markPlaylistCoverChanged(id, true);
    return playlist;
  },

  removePlaylistCover: async (id: string) => {
    await request<void>(`/playlists/${id}/cover`, { method: "DELETE" });
    markPlaylistCoverChanged(id, false);
  },

  recommendations: (sectionSize = 12) =>
    request<RecommendationHome>(`/recommendations/home${query({ sectionSize })}`),

  recommendedTracks: (params: PageParams = {}) =>
    request<Paged<RecommendedTrack>>(`/recommendations/tracks${query({ ...params })}`),

  recommendedArtists: (limit = 12) =>
    request<Artist[]>(`/recommendations/artists${query({ limit })}`),

  recommendedAlbums: (limit = 12) =>
    request<Album[]>(`/recommendations/albums${query({ limit })}`),

  similarTracks: (trackId: string, limit = 20) =>
    request<RecommendedTrack[]>(`/recommendations/similar/${trackId}${query({ limit })}`),

  history: (params: PageParams = {}) =>
    request<Paged<HistoryEntry>>(`/history${query({ ...params })}`),

  recentlyPlayed: (params: PageParams = {}) =>
    request<Paged<Track>>(`/history/recent${query({ ...params })}`),

  recordPlay: (trackId: string, playbackPosition: number) =>
    request<void>("/history", { method: "POST", body: { trackId, playbackPosition } }),

  clearHistory: () => request<void>("/history", { method: "DELETE" }),

  adminUsers: (params: PageParams = {}) =>
    request<Paged<AdminUser>>(`/admin/users${query({ ...params })}`),

  createUser: (body: {
    username: string;
    password: string;
    displayName?: string;
    isAdmin: boolean;
  }) => request<AdminUser>("/admin/users", { method: "POST", body }),

  updateArtist: (id: string, name: string) =>
    request<Artist>(`/artists/${id}`, { method: "PUT", body: { name } }),

  uploadArtistImage: async (id: string, file: File) => {
    const form = new FormData();
    form.append("file", file);

    const artist = await request<Artist>(`/artists/${id}/image`, { method: "POST", body: form });
    markArtistImageChanged(id, true);
    return artist;
  },

  removeArtistImage: async (id: string) => {
    await request<void>(`/artists/${id}/image`, { method: "DELETE" });
    markArtistImageChanged(id, false);
  },
};

async function uploadWithProgress(
  files: File[],
  onProgress?: (progress: UploadProgress) => void,
): Promise<UploadResult> {
  const totalBytes = files.reduce((sum, file) => sum + file.size, 0);

  const uploaded: UploadResult["uploaded"] = [];
  const failed: UploadResult["failed"] = [];

  let sentBytes = 0;

  for (const [index, file] of files.entries()) {
    const report = (fileLoaded: number) =>
      onProgress?.({
        percent: totalBytes === 0 ? 100 : Math.round(((sentBytes + fileLoaded) / totalBytes) * 100),
        fileIndex: index,
        fileCount: files.length,
        fileName: file.name,
      });

    report(0);

    try {
      const result = await uploadOneFile(file, report);
      uploaded.push(...result.uploaded);
      failed.push(...result.failed);
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) throw reason;

      failed.push({
        fileName: file.name,
        reason: reason instanceof Error ? reason.message : tr("upload.noConnection"),
      });
    }

    sentBytes += file.size;
    report(0);
  }

  return { uploaded, failed };
}

function uploadOneFile(file: File, onLoaded: (bytes: number) => void): Promise<UploadResult> {
  return new Promise((resolve, reject) => {
    const form = new FormData();
    form.append("files", file);

    const xhr = new XMLHttpRequest();
    xhr.open("POST", `${API_BASE}/tracks/upload`);
    xhr.withCredentials = true;

    xhr.upload.addEventListener("progress", (event) => {
      if (event.lengthComputable) onLoaded(event.loaded);
    });

    xhr.addEventListener("load", () => {
      let parsed: unknown = null;
      try {
        parsed = JSON.parse(xhr.responseText);
      } catch {}

      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(parsed as UploadResult);
        return;
      }

      if (xhr.status === 400 && parsed && typeof parsed === "object" && "failed" in parsed) {
        resolve(parsed as UploadResult);
        return;
      }

      if (GATEWAY_STATUSES.has(xhr.status)) {
        reject(new ApiError(xhr.status, tr("error.unreachable")));
        return;
      }

      const problem = parsed as { detail?: string; title?: string } | null;
      reject(new ApiError(xhr.status, problem?.detail ?? problem?.title ?? tr("upload.failedStatus", { status: xhr.status })));
    });

    xhr.addEventListener("error", () => reject(new ApiError(0, tr("upload.noConnection"))));
    xhr.addEventListener("abort", () => reject(new ApiError(0, tr("upload.cancelled"))));

    xhr.send(form);
  });
}

