import { API_BASE } from "@/lib/http";

/**
 * Поведенческая телеметрия для движка рекомендаций.
 *
 * События буферизуются и отправляются пачками. Перемотка по очереди рождает сигнал каждые пару
 * секунд, и запрос на каждый сигнал соперничал бы с аудиопотоком за то самое соединение, которое
 * нужно плееру.
 *
 * Доставка намеренно «по возможности»: сервер считает весь поток совещательным, поэтому потерянная
 * пачка стоит доли одного профиля и никогда не должна всплыть перед слушателем.
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

/** Откуда запустили воспроизведение. Повторяет серверное перечисление. */
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
 * Сессия прослушивания: одна вкладка браузера, от открытия до закрытия. Именно она делает два трека
 * «прослушанными вместе», поэтому намеренно переживает переходы по страницам, но не новую вкладку.
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

  // Страница, запущенная с домашнего экрана, ведёт себя достаточно иначе, чем вкладка браузера,
  // чтобы потом их стоило уметь различать.
  return window.matchMedia?.("(display-mode: standalone)").matches ? "pwa" : "web";
}

function attachListeners() {
  if (listenersAttached || typeof document === "undefined") return;
  listenersAttached = true;

  // Скрытие или закрытие вкладки — последняя возможность сообщить, что в ней произошло.
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "hidden") flushEvents();
  });

  window.addEventListener("pagehide", () => flushEvents());
}

/** Ставит одно событие в очередь. Отправка идёт по таймеру или сразу, как только буфер заполнен. */
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
 * Отправляет всё, что накопилось в буфере.
 *
 * По возможности использует `sendBeacon`, потому что обычный повод для явного сброса — уходящая
 * страница: начатый на ней `fetch` отменяется вместе с документом, и систематически терялись бы
 * последние несколько событий каждой сессии.
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
    // Телеметрия совещательная. Неудавшаяся пачка отбрасывается, а не повторяется: отправив её
    // позже, мы сообщили бы устаревшие метки времени и исказили ровно тот сигнал свежести,
    // который она и питает.
  });
}
