// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { AudioQuality, Track } from "@/lib/types";

export type OfflineQuality = Exclude<AudioQuality, "Original">;
export type OfflineDownloadState = "preparing" | "downloading" | "ready" | "failed";

export interface OfflineRecord {
  userId: string;
  track: Track;
  /** Тир, который реально лежит в кэше: мастер мог отдать только младшую готовую вариацию. */
  quality: OfflineQuality;
  /** Тир, который просил слушатель. По нему решаем, нужно ли качать заново. */
  requestedQuality: OfflineQuality;
  state: OfflineDownloadState;
  playlistUrl?: string;
  resourceUrls: string[];
  downloadedBytes: number;
  sourceEtag?: string;
  downloadedAt?: number;
  error?: string;
}

export interface OfflineSnapshot {
  tracks: OfflineRecord[];
}

export interface OfflineSource {
  playlistUrl: string;
  quality: OfflineQuality;
}

export interface OfflineStorageAdapter {
  load(userId: string): Promise<OfflineRecord[]>;
  save(record: OfflineRecord): Promise<void>;
  remove(record: OfflineRecord): Promise<void>;
  hasResource(url: string): Promise<boolean>;
  putResource(url: string, response: Response): Promise<number>;
  /** Убирает ресурсы, не трогая саму запись: нужно, когда трек перекачивается в другом тире. */
  dropResources(urls: string[]): Promise<void>;
  requestPersistence(): Promise<boolean>;
}

export interface OfflineLibrary {
  getSnapshot(): OfflineSnapshot;
  download(request: { tracks: Track[]; quality: OfflineQuality }): Promise<void>;
  remove(trackIds: string[]): Promise<void>;
  resolve(trackId: string): Promise<OfflineSource | null>;
  subscribe(listener: () => void): () => void;
}

interface OfflineLibraryDependencies {
  userId: string;
  storage: OfflineStorageAdapter;
  fetchMedia: (url: string | URL, init?: RequestInit) => Promise<Response>;
  wait?: (milliseconds: number) => Promise<void>;
  now?: () => number;
}

const CONCURRENCY = 2;
const PREPARATION_ATTEMPTS = 60;
const PREPARATION_POLL_MS = 2_000;

/**
 * Сколько ждать именно запрошенный тир, когда играбельный уже готов. Мастер перечисляет только
 * нарезанные вариации, а старшие бэкенд греет вне очереди — держать альбом в ожидании High
 * дороже, чем сохранить Normal и не заставлять слушателя смотреть на застывший прогресс.
 */
const UPGRADE_ATTEMPTS = 5;

/** Байты прикапываются после каждого сегмента, но перерисовывать на каждый из них — впустую. */
const PROGRESS_EMIT_MS = 400;

const QUALITY_ORDER: OfflineQuality[] = ["Low", "Normal", "High"];

interface PreparedVariant {
  variant: string;
  quality: OfflineQuality;
  etag?: string;
}

export async function createOfflineLibrary({
  userId,
  storage,
  fetchMedia,
  wait = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds)),
  now = () => Date.now(),
}: OfflineLibraryDependencies): Promise<OfflineLibrary> {
  const loaded = await storage.load(userId);
  const restored = await Promise.all(
    loaded.map(async (record) => {
      if (record.state !== "preparing" && record.state !== "downloading") return record;

      const interrupted: OfflineRecord = {
        ...record,
        state: "failed",
        error: "Download was interrupted.",
      };
      await storage.save(interrupted);
      return interrupted;
    }),
  );
  const records = new Map(restored.map((record) => [record.track.id, record]));
  const listeners = new Set<() => void>();
  const running = new Map<string, { promise: Promise<void>; controller: AbortController }>();

  let lastProgressEmit = 0;

  const emit = () => listeners.forEach((listener) => listener());

  /**
   * `progressOnly` — про запись, у которой сдвинулись только байты. Такие уведомления
   * прореживаются: смены состояния всегда доходят сразу, а значит последнее число в списке
   * всё равно точное. Без этого качание альбома перерисовывало каждое меню трека полсотни
   * раз на трек, ради цифры, которая меняется быстрее, чем её успевают прочитать.
   */
  const save = async (record: OfflineRecord, progressOnly = false) => {
    records.set(record.track.id, record);
    await storage.save(record);

    if (progressOnly) {
      const timestamp = now();
      if (timestamp - lastProgressEmit < PROGRESS_EMIT_MS) return;
      lastProgressEmit = timestamp;
    }

    emit();
  };

  const downloadOne = (track: Track, quality: OfflineQuality): Promise<void> => {
    const active = running.get(track.id);
    if (active) return active.promise;

    const existing = records.get(track.id);
    if (
      existing?.state === "ready" &&
      existing.requestedQuality === quality &&
      existing.playlistUrl
    ) {
      return Promise.resolve();
    }

    const controller = new AbortController();
    const work = (async () => {
      const previous = records.get(track.id);
      const record: OfflineRecord = {
        userId,
        track,
        quality: previous?.quality ?? quality,
        requestedQuality: quality,
        state: "preparing",
        // Ссылки на прошлую попытку переносим сразу: пока тир не выбран, только они и связывают
        // запись с тем, что уже лежит в Cache Storage. Оборвись загрузка здесь — remove() всё
        // ещё найдёт, что убирать.
        resourceUrls: previous ? [...previous.resourceUrls] : [],
        downloadedBytes: previous?.downloadedBytes ?? 0,
      };

      await save(record);

      try {
        const masterUrl = `/api/tracks/${track.id}/hls/master.m3u8?maxQuality=${quality}`;
        const prepared = await waitForVariant(
          masterUrl,
          quality,
          fetchMedia,
          wait,
          controller.signal,
        );

        // Догоняем прошлую попытку, только если она качала ровно тот же тир. У другого свои URL,
        // и его сегменты в Cache Storage больше никто не назовёт: remove() ходит по resourceUrls,
        // так что не выброшенные здесь остались бы мёртвым грузом до самого выхода из аккаунта.
        if (previous && previous.quality !== prepared.quality) {
          await storage.dropResources(previous.resourceUrls);
          record.resourceUrls = [];
          record.downloadedBytes = 0;
        }

        record.quality = prepared.quality;

        const playlistUrl = resolveResource(prepared.variant, masterUrl);
        const playlistResponse = await fetchMedia(playlistUrl, { signal: controller.signal });
        if (!playlistResponse.ok) {
          throw new Error(`Could not download the offline playlist (${playlistResponse.status}).`);
        }

        const playlist = await playlistResponse.text();
        record.state = "downloading";
        record.playlistUrl = playlistUrl;
        record.sourceEtag = prepared.etag;
        await save(record);

        await storeTextResource(
          storage,
          record,
          playlistUrl,
          playlist,
          playlistResponse.headers,
          save,
        );

        for (const resource of mediaResources(playlist)) {
          const url = resolveResource(resource, playlistUrl);
          if (await storage.hasResource(url)) {
            if (!record.resourceUrls.includes(url)) {
              record.resourceUrls.push(url);
              await save(record, true);
            }
            continue;
          }

          const response = await fetchMedia(url, { signal: controller.signal });
          if (!response.ok)
            throw new Error(`Could not download an offline segment (${response.status}).`);

          const bytes = await storage.putResource(url, response);
          record.resourceUrls.push(url);
          record.downloadedBytes += bytes;
          await save(record, true);
        }

        record.state = "ready";
        record.downloadedAt = now();
        record.error = undefined;
        await save(record);
      } catch (reason) {
        record.state = "failed";
        record.error = reason instanceof Error ? reason.message : "Offline download failed.";
        await save(record);
        throw reason;
      }
    })().finally(() => running.delete(track.id));

    running.set(track.id, { promise: work, controller });
    return work;
  };

  return {
    getSnapshot: () => ({ tracks: [...records.values()] }),

    async download({ tracks, quality }) {
      await storage.requestPersistence();

      let next = 0;
      const failures: unknown[] = [];
      const worker = async () => {
        while (next < tracks.length) {
          const track = tracks[next++];
          if (!track) continue;
          try {
            await downloadOne(track, quality);
          } catch (reason) {
            failures.push(reason);
          }
        }
      };

      await Promise.all(Array.from({ length: Math.min(CONCURRENCY, tracks.length) }, worker));
      if (failures.length > 0) throw failures[0];
    },

    async remove(trackIds) {
      for (const trackId of trackIds) {
        const active = running.get(trackId);
        active?.controller.abort();
        await active?.promise.catch(() => {});
        const record = records.get(trackId);
        if (!record) continue;
        await storage.remove(record);
        records.delete(trackId);
        emit();
      }
    },

    async resolve(trackId) {
      const record = records.get(trackId);
      if (record?.state !== "ready" || !record.playlistUrl) return null;
      return { playlistUrl: record.playlistUrl, quality: record.quality };
    },

    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
  };
}

async function waitForVariant(
  masterUrl: string,
  quality: OfflineQuality,
  fetchMedia: (url: string | URL, init?: RequestInit) => Promise<Response>,
  wait: (milliseconds: number) => Promise<void>,
  signal: AbortSignal,
): Promise<PreparedVariant> {
  let fallback: PreparedVariant | null = null;
  let fallbackAttempts = 0;

  for (let attempt = 0; attempt < PREPARATION_ATTEMPTS; attempt += 1) {
    signal.throwIfAborted();
    const response = await fetchMedia(masterUrl, { signal });

    // 202 — «ещё режу, ничего играбельного нет»; всё остальное вне 2xx безнадёжно.
    if (response.status !== 202) {
      if (!response.ok) {
        throw new Error(`Could not prepare the offline rendition (${response.status}).`);
      }

      const etag = response.headers.get("ETag") ?? undefined;
      const best = bestVariant(await response.text(), quality);

      if (best?.quality === quality) return { ...best, etag };
      if (best) {
        // Мастер отдаёт 200 уже с одной готовой вариацией, так что младший тир здесь — норма,
        // а не сбой. Немного ждём запрошенный и, если он не доезжает, забираем что есть:
        // лучше сохранить Normal сейчас, чем свалиться через две минуты вообще без копии.
        fallback = { ...best, etag };
        if ((fallbackAttempts += 1) >= UPGRADE_ATTEMPTS) return fallback;
      }
    }

    if (attempt + 1 < PREPARATION_ATTEMPTS) await wait(PREPARATION_POLL_MS);
  }

  if (fallback) return fallback;
  throw new Error(`The ${quality} offline rendition is not ready.`);
}

async function storeTextResource(
  storage: OfflineStorageAdapter,
  record: OfflineRecord,
  url: string,
  body: string,
  headers: Headers,
  save: (record: OfflineRecord) => Promise<void>,
): Promise<void> {
  if (await storage.hasResource(url)) return;

  const bytes = await storage.putResource(url, new Response(body, { status: 200, headers }));
  record.resourceUrls.push(url);
  record.downloadedBytes += bytes;
  await save(record);
}

/** Самый старший готовый тир, не превышающий запрошенный. */
function bestVariant(
  master: string,
  cap: OfflineQuality,
): { variant: string; quality: OfflineQuality } | null {
  const uris = playlistUris(master);

  for (let index = QUALITY_ORDER.indexOf(cap); index >= 0; index -= 1) {
    const quality = QUALITY_ORDER[index];
    if (!quality) continue;

    const suffix = `${quality.toLowerCase()}/index.m3u8`;
    const variant = uris.find((uri) => uri.toLowerCase().endsWith(suffix));
    if (variant) return { variant, quality };
  }

  return null;
}

function mediaResources(playlist: string): string[] {
  const init = /#EXT-X-MAP:URI="([^"]+)"/.exec(playlist)?.[1];
  const segments = playlistUris(playlist);
  return init ? [init, ...segments] : segments;
}

function playlistUris(playlist: string): string[] {
  return playlist
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0 && !line.startsWith("#"));
}

function resolveResource(resource: string, base: string): string {
  const origin = "https://offline.invalid";
  const resolved = new URL(resource, new URL(base, origin));
  return resolved.origin === origin
    ? `${resolved.pathname}${resolved.search}`
    : resolved.toString();
}
