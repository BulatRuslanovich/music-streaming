// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { mediaUrl } from "@/lib/media";
import { fetchMedia } from "@/lib/http";
import type { AdaptiveQuality } from "@/lib/adaptivePlayback";

const STABLE_WINDOW_MS = 30_000;
let shellCachePromise: Promise<void> | null = null;

/** Сегментов в разгоне: при четырёхсекундной нарезке это около половины минуты звучания. */
export const HEAD_START_SEGMENTS = 6;

/** Сколько сегментов тянем одновременно — чтобы не выстроить шестьдесят запросов в цепочку. */
const SEGMENT_CONCURRENCY = 3;

type PrefetchStage = "none" | "headStart" | "full";

interface PrefetchReadiness {
  online: boolean;
  playing: boolean;
  position: number;
  bufferedUntil: number;
  duration: number;
  lastStallAt: number;
  now: number;
}

/**
 * Насколько далеко можно забегать вперёд.
 *
 * Раньше здесь было одно условие — шестьдесят секунд буфера впереди, — и на узком канале оно не
 * выполнялось никогда: префетча не было ровно там, где он нужен. Поэтому стадии две. Разгон
 * (начало следующего трека) не требует запаса вообще: он стоит десятков килобайт и убирает паузу
 * на переходе. Полная догрузка по-прежнему ждёт, пока сеть докажет, что справляется.
 */
export function prefetchStage(state: PrefetchReadiness): PrefetchStage {
  if (!state.online || !state.playing || state.duration <= 0) return "none";
  if (state.lastStallAt > 0 && state.now - state.lastStallAt < STABLE_WINDOW_MS) return "none";

  const remaining = Math.max(0, state.duration - state.position);
  const required = Math.min(60, remaining);

  return state.bufferedUntil - state.position >= required ? "full" : "headStart";
}

export async function prefetchHlsTracks(
  trackIds: string[],
  quality: AdaptiveQuality,
  signal: AbortSignal,
  segmentLimit?: number,
): Promise<boolean> {
  for (const trackId of trackIds) {
    if (!(await prefetchTrack(trackId, quality, signal, segmentLimit))) return false;
  }
  return true;
}

export function pinStreamTracks(trackIds: string[]): void {
  postToStreamWorker({ type: "pin-stream-tracks", trackIds });
}

/**
 * Стирает всё, что накоплено под конкретного слушателя: поток, данные API и обложки.
 *
 * Вызывается на выходе из аккаунта. Чистим и здесь, и в service worker: на выходе он может быть
 * ещё не активен, а оставить чужую библиотеку в кэше нельзя ни в каком случае.
 */
export async function clearStreamCache(): Promise<void> {
  shellCachePromise = null;
  if ("caches" in window) {
    await Promise.all([
      caches.delete("caimack-shell-v1"),
      caches.delete("caimack-hls-v1"),
      caches.delete("caimack-offline-media-v1"),
      caches.delete("caimack-data-v1"),
      caches.delete("caimack-images-v1"),
    ]);
  }
  if ("indexedDB" in window) {
    await Promise.all([
      deleteBrowserDatabase("caimack-stream-cache"),
      deleteBrowserDatabase("caimack-offline"),
      deleteBrowserDatabase("caimack-offline-v1"),
      deleteBrowserDatabase("caimack-event-outbox-v1"),
    ]);
  }
  postToStreamWorker({ type: "clear-stream-cache" });
}

/** Гарантирует оболочку для первого офлайн-запуска, когда SW не видел начальную навигацию. */
export function cacheAppShell(): Promise<void> {
  if (typeof window === "undefined" || !("caches" in window) || !("serviceWorker" in navigator)) {
    return Promise.resolve();
  }

  shellCachePromise ??= (async () => {
    registerStreamWorker();
    await navigator.serviceWorker.ready;

    const request = new Request("/", {
      credentials: "include",
      headers: { Accept: "text/html" },
    });
    const response = await fetch(request);
    if (!response.ok || !response.headers.get("Content-Type")?.includes("text/html")) return;

    const cache = await caches.open("caimack-shell-v1");
    await cache.put(request, response);
  })().catch(() => {
    shellCachePromise = null;
  });

  return shellCachePromise;
}

function postToStreamWorker(message: unknown): void {
  if (!("serviceWorker" in navigator)) return;
  if (navigator.serviceWorker.controller) {
    navigator.serviceWorker.controller.postMessage(message);
    return;
  }

  void navigator.serviceWorker.ready.then((registration) =>
    registration.active?.postMessage(message),
  );
}

function deleteBrowserDatabase(name: string): Promise<void> {
  return new Promise((resolve) => {
    const request = indexedDB.deleteDatabase(name);
    request.onsuccess = () => resolve();
    request.onerror = () => resolve();
    request.onblocked = () => resolve();
  });
}

export function registerStreamWorker(): void {
  if ("serviceWorker" in navigator) {
    void navigator.serviceWorker.register("/sw.js", { scope: "/" }).catch(() => {});
  }
}

async function prefetchTrack(
  trackId: string,
  quality: AdaptiveQuality,
  signal: AbortSignal,
  segmentLimit?: number,
): Promise<boolean> {
  try {
    const master = await fetchMedia(mediaUrl.hls(trackId, quality), { signal });
    if (!master.ok || master.status === 202) return false;

    const masterText = await master.text();
    const variants = playlistUris(masterText);
    // Мастер может не содержать запрошенной ступени: готова ещё не всякая. Берём ту, что есть.
    const suffix = `${quality.toLowerCase()}/index.m3u8`;
    const variant = variants.find((uri) => uri.toLowerCase().endsWith(suffix)) ?? variants[0];
    if (!variant) return false;

    const media = await fetchMedia(new URL(variant, master.url), { signal });
    if (!media.ok) return false;

    const mediaText = await media.text();
    const base = media.url;
    const init = /#EXT-X-MAP:URI="([^"]+)"/.exec(mediaText)?.[1];
    const segments = playlistUris(mediaText);
    const wanted = segmentLimit === undefined ? segments : segments.slice(0, segmentLimit);

    // init.mp4 обязателен и идёт первым: без него сегменты бесполезны.
    if (init && !(await fetchInto(new URL(init, base), signal))) return false;

    return await fetchAll(
      wanted.map((resource) => new URL(resource, base)),
      signal,
    );
  } catch (reason) {
    if (reason instanceof DOMException && reason.name === "AbortError") throw reason;
    return false;
  }
}

async function fetchAll(urls: URL[], signal: AbortSignal): Promise<boolean> {
  let next = 0;
  let ok = true;

  const worker = async () => {
    while (ok && next < urls.length) {
      const url = urls[next++];
      if (!(await fetchInto(url, signal))) ok = false;
    }
  };

  await Promise.all(Array.from({ length: Math.min(SEGMENT_CONCURRENCY, urls.length) }, worker));

  return ok;
}

async function fetchInto(url: URL, signal: AbortSignal): Promise<boolean> {
  const response = await fetchMedia(url, { signal });
  if (!response.ok) return false;
  await response.arrayBuffer();
  return true;
}

function playlistUris(playlist: string): string[] {
  return playlist
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0 && !line.startsWith("#"));
}
