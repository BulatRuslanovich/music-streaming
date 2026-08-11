export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

const API_BASE = "/api";

let refreshInFlight: Promise<boolean> | null = null;

type SessionExpiredListener = () => void;
const sessionExpiredListeners = new Set<SessionExpiredListener>();

export function onSessionExpired(listener: SessionExpiredListener): () => void {
  sessionExpiredListeners.add(listener);
  return () => sessionExpiredListeners.delete(listener);
}

async function refreshSession(): Promise<boolean> {
  refreshInFlight ??= (async () => {
    try {
      const response = await fetch(`${API_BASE}/auth/refresh`, {
        method: "POST",
        credentials: "include",
      });
      return response.ok;
    } catch {
      return false;
    } finally {
      setTimeout(() => {
        refreshInFlight = null;
      }, 0);
    }
  })();

  return refreshInFlight;
}

interface RequestOptions {
  method?: string;
  body?: unknown;
  signal?: AbortSignal;
  isRetry?: boolean;
  allowUnauthenticated?: boolean;
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = "GET", body, signal, isRetry = false, allowUnauthenticated = false } = options;

  const init: RequestInit = { method, credentials: "include", signal };

  if (body instanceof FormData) {
    init.body = body;
  } else if (body !== undefined) {
    init.headers = { "Content-Type": "application/json" };
    init.body = JSON.stringify(body);
  }

  const response = await fetch(`${API_BASE}${path}`, init);

  if (response.status === 401 && !isRetry) {
    if (await refreshSession()) {
      return request<T>(path, { ...options, isRetry: true });
    }

    if (!allowUnauthenticated) {
      sessionExpiredListeners.forEach((listener) => listener());
    }

    throw new ApiError(401, "Your session has expired. Please sign in again.");
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readErrorMessage(response));
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

async function readErrorMessage(response: Response): Promise<string> {
  try {
    const text = await response.text();
    if (!text) {
      if (response.status === 403) return "You do not have permission to do this.";
      return response.statusText || `Request failed (${response.status})`;
    }

    const parsed = JSON.parse(text) as { detail?: string; title?: string };
    return parsed.detail ?? parsed.title ?? text;
  } catch {
    return response.statusText || `Request failed (${response.status})`;
  }
}

function query(params: Record<string, string | number | boolean | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== "") search.set(key, String(value));
  }
  const asString = search.toString();
  return asString ? `?${asString}` : "";
}

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

export const api = {
  login: (username: string, password: string) =>
    request<User>("/auth/login", { method: "POST", body: { username, password } }),

  logout: () => request<void>("/auth/logout", { method: "POST" }),

  me: () => request<User>("/auth/me", { allowUnauthenticated: true }),

  config: () => request<ClientConfig>("/config"),

  home: (sectionSize = 12) => request<HomeSummary>(`/home${query({ sectionSize })}`),

  tracks: (params: PageParams & { sort?: TrackSort } = {}) =>
    request<Paged<Track>>(`/tracks${query({ ...params })}`),

  track: (id: string) => request<Track>(`/tracks/${id}`),

  artists: (params: PageParams = {}) => request<Paged<Artist>>(`/artists${query({ ...params })}`),

  artist: (id: string) => request<ArtistDetail>(`/artists/${id}`),

  albums: (params: PageParams & { artistId?: string; recentFirst?: boolean } = {}) =>
    request<Paged<Album>>(`/albums${query({ ...params })}`),

  album: (id: string) => request<AlbumDetail>(`/albums/${id}`),

  genres: () => request<Genre[]>("/genres"),

  genreTracks: (id: string, params: PageParams = {}) =>
    request<Paged<Track>>(`/genres/${id}/tracks${query({ ...params })}`),

  search: (q: string, limit = 20) => request<SearchResults>(`/search${query({ q, limit })}`),

  upload: (files: File[], onProgress?: (percent: number) => void) =>
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

  uploadArtistImage: (id: string, file: File) => {
    const form = new FormData();
    form.append("file", file);
    return request<Artist>(`/artists/${id}/image`, { method: "POST", body: form });
  },

  removeArtistImage: (id: string) => request<void>(`/artists/${id}/image`, { method: "DELETE" }),
};

function uploadWithProgress(files: File[], onProgress?: (percent: number) => void): Promise<UploadResult> {
  return new Promise((resolve, reject) => {
    const form = new FormData();
    files.forEach((file) => form.append("files", file));

    const xhr = new XMLHttpRequest();
    xhr.open("POST", `${API_BASE}/tracks/upload`);
    xhr.withCredentials = true;

    xhr.upload.addEventListener("progress", (event) => {
      if (event.lengthComputable && onProgress) {
        onProgress(Math.round((event.loaded / event.total) * 100));
      }
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

      const problem = parsed as { detail?: string; title?: string } | null;
      reject(new ApiError(xhr.status, problem?.detail ?? problem?.title ?? `Upload failed (${xhr.status})`));
    });

    xhr.addEventListener("error", () => reject(new ApiError(0, "The upload could not reach the server.")));
    xhr.addEventListener("abort", () => reject(new ApiError(0, "The upload was cancelled.")));

    xhr.send(form);
  });
}

export const mediaUrl = {
  stream: (trackId: string) => `${API_BASE}/tracks/${trackId}/stream`,
  trackCover: (trackId: string) => `${API_BASE}/tracks/${trackId}/cover`,
  albumCover: (albumId: string) => `${API_BASE}/albums/${albumId}/cover`,
  artistImage: (artistId: string) => `${API_BASE}/artists/${artistId}/image`,
};

export function artistImageUrl({
  artistId,
  hasImage = true,
}: {
  artistId?: string | null;
  hasImage?: boolean;
}): string | null {
  return hasImage && artistId ? mediaUrl.artistImage(artistId) : null;
}

export function coverUrl({
  albumId,
  trackId,
  hasCover = true,
}: {
  albumId?: string | null;
  trackId?: string | null;
  hasCover?: boolean;
}): string | null {
  if (!hasCover) return null;
  if (albumId) return mediaUrl.albumCover(albumId);
  if (trackId) return mediaUrl.trackCover(trackId);
  return null;
}

export function trackCoverUrl(track: Track | null | undefined): string | null {
  if (!track) return null;
  return coverUrl({ albumId: track.albumId, trackId: track.id, hasCover: track.hasCover });
}
