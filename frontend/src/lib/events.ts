import { API_BASE } from "@/lib/http";

export type PlaybackEventType =
  | "trackStarted"
  | "trackPlayed"
  | "trackCompleted"
  | "trackSkipped"
  | "trackPaused"
  | "trackReplayed"
  | "trackLiked"
  | "trackUnliked"
  | "trackAddedToPlaylist"
  | "trackRemovedFromPlaylist"
  | "trackAddedToQueue"
  | "artistOpened"
  | "albumOpened"
  | "searchResultClicked"
  | "playlistOpened";

export type PlaybackSource =
  | "unknown"
  | "home"
  | "recommendation"
  | "search"
  | "album"
  | "artist"
  | "playlist"
  | "favorites"
  | "genre"
  | "history"
  | "queue"
  | "tracks"
  | "radio";

export interface PlaybackEventInput {
  type: PlaybackEventType;
  trackId?: string;
  entityId?: string;
  positionSeconds?: number;
  listenedSeconds?: number;
  durationSeconds?: number;
  source?: PlaybackSource;
  sourceId?: string;
}

interface QueuedEvent extends PlaybackEventInput {
  occurredAt: string;
  sessionId: string;
  platform: string;
}

const SESSION_STORAGE_KEY = "caimack.session";
const FLUSH_INTERVAL_MS = 10_000;
const MAX_BUFFERED = 100;

let buffer: QueuedEvent[] = [];
let flushTimer: ReturnType<typeof setTimeout> | null = null;
let listenersAttached = false;

export function deviceId(): string {
  if (typeof window === "undefined") return "";

  let id = window.sessionStorage.getItem(SESSION_STORAGE_KEY);
  if (!id) {
    id = crypto.randomUUID();
    window.sessionStorage.setItem(SESSION_STORAGE_KEY, id);
  }

  return id;
}

function platform(): string {
  if (typeof window === "undefined") return "web";

  return window.matchMedia?.("(display-mode: standalone)").matches ? "pwa" : "web";
}

function attachListeners() {
  if (listenersAttached || typeof document === "undefined") return;
  listenersAttached = true;

  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "hidden") flushEvents();
  });

  window.addEventListener("pagehide", () => flushEvents());
}

export function recordEvent(event: PlaybackEventInput): void {
  if (typeof window === "undefined") return;

  attachListeners();

  buffer.push({
    ...event,
    occurredAt: new Date().toISOString(),
    sessionId: deviceId(),
    platform: platform(),
  });

  if (buffer.length >= MAX_BUFFERED) {
    flushEvents();
    return;
  }

  flushTimer ??= setTimeout(() => {
    flushTimer = null;
    flushEvents();
  }, FLUSH_INTERVAL_MS);
}

export function flushEvents(): void {
  if (typeof window === "undefined" || buffer.length === 0) return;

  if (flushTimer !== null) {
    clearTimeout(flushTimer);
    flushTimer = null;
  }

  const events = buffer;
  buffer = [];

  const body = JSON.stringify({ events });
  const url = `${API_BASE}/events`;

  if (navigator.sendBeacon?.(url, new Blob([body], { type: "application/json" }))) {
    return;
  }

  void fetch(url, {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body,
    keepalive: true,
  }).catch(() => {});
}
