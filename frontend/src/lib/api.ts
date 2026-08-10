/**
 * Typed client for the music API.
 *
 * Authentication rides on HttpOnly cookies, so requests carry no tokens in JavaScript — they only
 * need `credentials: "include"`. When an access token expires the client transparently refreshes
 * once and replays the original request; concurrent 401s share a single refresh so a page with
 * several parallel requests does not fire a burst of them.
 */

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

/** Same-origin in production behind the reverse proxy; rewritten to the API in development. */
const API_BASE = "/api";

let refreshInFlight: Promise<boolean> | null = null;

/** Notifies the app that the session is gone, so the shell can send the user to /login. */
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
      // Cleared on the next tick so callers awaiting this promise all see the same result.
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
  /** Set internally to stop a refresh loop; not meant for callers. */
  isRetry?: boolean;
  /** Skips the redirect-to-login side effect, used by the initial session probe. */
  allowUnauthenticated?: boolean;
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = "GET", body, signal, isRetry = false, allowUnauthenticated = false } = options;

  const init: RequestInit = { method, credentials: "include", signal };

  if (body instanceof FormData) {
    // Let the browser set the multipart boundary itself.
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

/** Prefers the `detail` of an RFC 7807 problem response, falling back to the status text. */
async function readErrorMessage(response: Response): Promise<string> {
  try {
    const text = await response.text();
    if (!text) return response.statusText || `Request failed (${response.status})`;

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
  // --- authentication ---------------------------------------------------------------------
  login: (username: string, password: string) =>
    request<User>("/auth/login", { method: "POST", body: { username, password } }),

  logout: () => request<void>("/auth/logout", { method: "POST" }),

  me: () => request<User>("/auth/me", { allowUnauthenticated: true }),

  config: () => request<ClientConfig>("/config"),

  // --- library ---------------------------------------------------------------------------
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

  // --- track management ------------------------------------------------------------------
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

  // --- favourites -----------------------------------------------------------------------
  favorites: (params: PageParams = {}) => request<Paged<Track>>(`/favorites${query({ ...params })}`),

  addFavorite: (trackId: string) => request<void>(`/tracks/${trackId}/favorite`, { method: "POST" }),

  removeFavorite: (trackId: string) =>
    request<void>(`/tracks/${trackId}/favorite`, { method: "DELETE" }),

  // --- playlists ------------------------------------------------------------------------
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

  // --- history --------------------------------------------------------------------------
  history: (params: PageParams = {}) =>
    request<Paged<HistoryEntry>>(`/history${query({ ...params })}`),

  recentlyPlayed: (params: PageParams = {}) =>
    request<Paged<Track>>(`/history/recent${query({ ...params })}`),

  recordPlay: (trackId: string, playbackPosition: number) =>
    request<void>("/history", { method: "POST", body: { trackId, playbackPosition } }),

  clearHistory: () => request<void>("/history", { method: "DELETE" }),
};

/**
 * Uploads via XHR rather than fetch: a large batch of MP3s needs a real progress bar, and
 * `fetch` still cannot report request upload progress.
 */
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
      } catch {
        /* handled below */
      }

      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(parsed as UploadResult);
        return;
      }

      // A batch where every file failed comes back as 400 with the same shape, which is more
      // useful to show than a bare status code.
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

/** URLs the browser fetches directly (audio element, image tags). */
export const mediaUrl = {
  stream: (trackId: string) => `${API_BASE}/tracks/${trackId}/stream`,
  trackCover: (trackId: string) => `${API_BASE}/tracks/${trackId}/cover`,
  albumCover: (albumId: string) => `${API_BASE}/albums/${albumId}/cover`,
};
