// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { API_BASE, refreshSession } from "@/lib/http";
import { BrowserEventOutboxStorage } from "@/lib/browserEventOutbox";
import { createEventOutbox, type EventOutbox } from "@/lib/eventOutbox";

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
  | "radio"
  | "dj";

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
let flushTimer: ReturnType<typeof setTimeout> | null = null;
let listenersAttached = false;
let outbox: EventOutbox<QueuedEvent> | null = null;

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
  window.addEventListener("online", () => flushEvents());
  flushEvents();
}

export function recordEvent(event: PlaybackEventInput): void {
  if (typeof window === "undefined") return;

  attachListeners();

  const eventWithContext: QueuedEvent = {
    ...event,
    occurredAt: new Date().toISOString(),
    sessionId: deviceId(),
    platform: platform(),
  };

  void getOutbox()
    .add(eventWithContext)
    .catch(() => {});

  flushTimer ??= setTimeout(() => {
    flushTimer = null;
    flushEvents();
  }, FLUSH_INTERVAL_MS);
}

function flushEvents(): void {
  if (typeof window === "undefined") return;

  if (flushTimer !== null) {
    clearTimeout(flushTimer);
    flushTimer = null;
  }

  void getOutbox()
    .flush()
    .catch(() => {});
}

function getOutbox(): EventOutbox<QueuedEvent> {
  outbox ??= createEventOutbox({
    storage: new BrowserEventOutboxStorage<QueuedEvent>(),
    isOnline: () => navigator.onLine,
    send: async (events) => {
      const body = JSON.stringify({ events });
      // Не `/events` — блокировщики рекламы считают такой путь аналитикой и режут запрос.
      const post = () =>
        fetch(`${API_BASE}/playback/signals`, {
          method: "POST",
          credentials: "include",
          headers: { "Content-Type": "application/json" },
          body,
          keepalive: true,
        });

      try {
        const response = await post();
        if (response.status !== 401) return response.ok;

        // Пока буфер жил в памяти, 401 просто терял партию. Теперь она лежит в IndexedDB и
        // будет проситься наружу до конца сессии, поэтому истёкший доступ надо обновить —
        // сырой fetch мимо `send` про единый refresh сам не знает.
        await response.body?.cancel().catch(() => {});
        return (await refreshSession()) ? (await post()).ok : false;
      } catch {
        return false;
      }
    },
  });

  return outbox;
}
