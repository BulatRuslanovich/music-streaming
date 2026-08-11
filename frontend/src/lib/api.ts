import { tr } from "@/lib/i18n";
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

const GATEWAY_STATUSES = new Set([502, 503, 504, 520, 521, 522, 523, 524, 525, 526, 527, 530]);

const RETRY_DELAYS_MS = [400, 1200];

function isAbort(reason: unknown): boolean {
  return reason instanceof DOMException && reason.name === "AbortError";
}

function delay(milliseconds: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    const timer = setTimeout(resolve, milliseconds);
    signal?.addEventListener("abort", () => {
      clearTimeout(timer);
      resolve();
    }, { once: true });
  });
}

async function fetchWithRetry(url: string, init: RequestInit, retryable: boolean): Promise<Response> {
  let lastReason: unknown = null;

  for (let attempt = 0; ; attempt += 1) {
    try {
      const response = await fetch(url, init);

      if (!retryable || !GATEWAY_STATUSES.has(response.status) || attempt >= RETRY_DELAYS_MS.length) {
        return response;
      }
    } catch (reason) {
      if (isAbort(reason) || !retryable || attempt >= RETRY_DELAYS_MS.length) throw reason;
      lastReason = reason;
    }

    await delay(RETRY_DELAYS_MS[attempt], init.signal ?? undefined);

    if (init.signal?.aborted) {
      throw lastReason ?? new DOMException("Aborted", "AbortError");
    }
  }
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

  const response = await fetchWithRetry(`${API_BASE}${path}`, init, method === "GET");

  if (response.status === 401 && !isRetry) {
    if (await refreshSession()) {
      return request<T>(path, { ...options, isRetry: true });
    }

    if (!allowUnauthenticated) {
      sessionExpiredListeners.forEach((listener) => listener());
    }

    throw new ApiError(401, tr("error.sessionExpired"));
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
  if (GATEWAY_STATUSES.has(response.status)) {
    return tr("error.unreachable");
  }

  try {
    const text = await response.text();
    if (!text) {
      if (response.status === 403) return tr("error.forbidden");
      return response.statusText || tr("error.requestFailed", { status: response.status });
    }

    const parsed = JSON.parse(text) as { detail?: string; title?: string };
    return parsed.detail ?? parsed.title ?? text;
  } catch {
    return response.statusText || tr("error.requestFailed", { status: response.status });
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

  tracks: (params: PageParams & { sort?: TrackSort } = {}) =>
    request<Paged<Track>>(`/tracks${query({ ...params })}`),

  track: (id: string) => request<Track>(`/tracks/${id}`),

  artists: (params: PageParams = {}) => request<Paged<Artist>>(`/artists${query({ ...params })}`),

  artist: (id: string, params: PageParams = {}) =>
    request<ArtistDetail>(`/artists/${id}${query({ ...params })}`),

  albums: (params: PageParams & { artistId?: string; recentFirst?: boolean } = {}) =>
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

  uploadArtistImage: async (id: string, file: File) => {
    const form = new FormData();
    form.append("file", file);

    const artist = await request<Artist>(`/artists/${id}/image`, { method: "POST", body: form });
    artistImageVersions.set(id, Date.now());
    return artist;
  },

  removeArtistImage: async (id: string) => {
    await request<void>(`/artists/${id}/image`, { method: "DELETE" });
    artistImageVersions.delete(id);
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

export type CoverVariant = "thumb" | "full";

function sizeQuery(variant: CoverVariant): string {
  return variant === "thumb" ? "?size=thumb" : "";
}

export type AudioQuality = "original" | "low";

export const mediaUrl = {
  stream: (trackId: string, quality: AudioQuality = "original") =>
    `${API_BASE}/tracks/${trackId}/stream${quality === "low" ? "?quality=low" : ""}`,
  trackCover: (trackId: string, variant: CoverVariant = "full") =>
    `${API_BASE}/tracks/${trackId}/cover${sizeQuery(variant)}`,
  albumCover: (albumId: string, variant: CoverVariant = "full") =>
    `${API_BASE}/albums/${albumId}/cover${sizeQuery(variant)}`,
  artistImage: (artistId: string) => `${API_BASE}/artists/${artistId}/image`,
};

const artistImageVersions = new Map<string, number>();

export function artistImageUrl({
  artistId,
  hasImage = true,
}: {
  artistId?: string | null;
  hasImage?: boolean;
}): string | null {
  if (!hasImage || !artistId) return null;

  const version = artistImageVersions.get(artistId);
  return version === undefined
    ? mediaUrl.artistImage(artistId)
    : `${mediaUrl.artistImage(artistId)}?v=${version}`;
}

export function coverUrl({
  albumId,
  trackId,
  hasCover = true,
  variant = "full",
}: {
  albumId?: string | null;
  trackId?: string | null;
  hasCover?: boolean;
  variant?: CoverVariant;
}): string | null {
  if (!hasCover) return null;
  if (albumId) return mediaUrl.albumCover(albumId, variant);
  if (trackId) return mediaUrl.trackCover(trackId, variant);
  return null;
}

export function trackCoverUrl(
  track: Track | null | undefined,
  variant: CoverVariant = "full",
): string | null {
  if (!track) return null;
  return coverUrl({ albumId: track.albumId, trackId: track.id, hasCover: track.hasCover, variant });
}
