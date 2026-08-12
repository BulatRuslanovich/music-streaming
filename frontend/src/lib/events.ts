import { API_BASE } from "@/lib/http";

/**
 * Behavioural telemetry for the recommendation engine.
 *
 * Events are buffered and sent in batches. Skipping through a queue produces a signal every couple
 * of seconds, and a request per signal would compete with the audio stream for the connection the
 * player actually needs.
 *
 * Delivery is best-effort by design: the server treats the whole feed as advisory, so a dropped
 * batch costs a fraction of one profile and must never surface to the listener.
 */

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

/** Where playback was started from. Mirrors the server's enum. */
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

/**
 * The listening session: one browser tab, from open to close. It is what makes two tracks count
 * as "played together", so it deliberately survives navigation but not a new tab.
 */
function sessionId(): string {
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

  // A page running from the home screen behaves differently enough from a browser tab that it is
  // worth being able to tell them apart later.
  return window.matchMedia?.("(display-mode: standalone)").matches ? "pwa" : "web";
}

function attachListeners() {
  if (listenersAttached || typeof document === "undefined") return;
  listenersAttached = true;

  // A tab being hidden or closed is the last chance to report what happened in it.
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "hidden") flushEvents();
  });

  window.addEventListener("pagehide", () => flushEvents());
}

/** Queues one event. Sending happens on a timer, or immediately once the buffer fills up. */
export function recordEvent(event: PlaybackEventInput): void {
  if (typeof window === "undefined") return;

  attachListeners();

  buffer.push({
    ...event,
    occurredAt: new Date().toISOString(),
    sessionId: sessionId(),
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

/**
 * Sends whatever is buffered.
 *
 * Uses `sendBeacon` when available, because the common case for an explicit flush is a page that
 * is going away — a `fetch` started there is cancelled with the document, and the last few events
 * of every session would be the ones systematically lost.
 */
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
  }).catch(() => {
    // Telemetry is advisory. A failed batch is dropped rather than retried: replaying it later
    // would report stale timestamps and distort exactly the recency signal it feeds.
  });
}
