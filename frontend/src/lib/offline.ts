import { mediaUrl } from "@/lib/media";
import type { AudioQuality, Track } from "@/lib/types";

/**
 * Хранилище скачанной музыки.
 *
 * Аудио лежит в Cache Storage, а не в IndexedDB и тем более не в состоянии React: это единственное
 * хранилище, из которого service worker может ответить на обычный запрос за файлом. Благодаря
 * этому офлайн-воспроизведение не требует от плеера ровно ничего — он запрашивает тот же адрес,
 * что и всегда, а откуда придут байты, решает service worker.
 *
 * В IndexedDB остаются только описания треков: без них офлайн нечего показать в списке, а держать
 * их в localStorage значит упереться в его несколько мегабайт на первой же сотне треков.
 */

const DATABASE = "caimack-offline";
const STORE = "tracks";
const VERSION = 1;

// Совпадает с именем в public/sw.js: worker — отдельный файл без общих импортов, поэтому имя
// приходится держать в двух местах.
const AUDIO_CACHE = "caimack-audio-v1";

/** Скачанный трек: само описание плюс то, чем оно стало на диске. */
export interface OfflineTrack {
  track: Track;
  quality: AudioQuality;
  bytes: number;
  savedAt: number;
}

export function offlineSupported(): boolean {
  return typeof window !== "undefined" && "caches" in window && "indexedDB" in window;
}

function open(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DATABASE, VERSION);

    request.onupgradeneeded = () => {
      if (!request.result.objectStoreNames.contains(STORE)) {
        request.result.createObjectStore(STORE, { keyPath: "track.id" });
      }
    };

    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

function run<T>(
  mode: IDBTransactionMode,
  action: (store: IDBObjectStore) => IDBRequest<T>,
): Promise<T> {
  return open().then(
    (db) =>
      new Promise<T>((resolve, reject) => {
        const transaction = db.transaction(STORE, mode);
        const request = action(transaction.objectStore(STORE));

        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
        transaction.oncomplete = () => db.close();
      }),
  );
}

export async function listOffline(): Promise<OfflineTrack[]> {
  if (!offlineSupported()) return [];

  const saved = await run<OfflineTrack[]>("readonly", (store) => store.getAll());
  return saved.sort((left, right) => right.savedAt - left.savedAt);
}

/**
 * Скачивает трек в том качестве, которое выбрано сейчас.
 *
 * Ответ читается по частям ради индикатора прогресса и только потом кладётся в кэш: недокачанный
 * файл в кэше выглядел бы как готовый и молча ломал бы воспроизведение.
 */
export async function downloadTrack(
  track: Track,
  quality: AudioQuality,
  onProgress?: (fraction: number) => void,
): Promise<OfflineTrack> {
  if (!offlineSupported()) throw new Error("offline-unsupported");

  const url = mediaUrl.stream(track.id, quality);
  const response = await fetch(url, { credentials: "include" });

  if (!response.ok || !response.body) throw new Error(`download-failed-${response.status}`);

  const expected = Number(response.headers.get("Content-Length") ?? 0);
  const reader = response.body.getReader();
  const chunks: Uint8Array[] = [];
  let received = 0;

  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;

    chunks.push(value);
    received += value.length;

    if (expected > 0) onProgress?.(Math.min(1, received / expected));
  }

  const body = new Blob(chunks as BlobPart[], {
    type: response.headers.get("Content-Type") ?? "audio/mpeg",
  });

  // Заголовки пересобираются, а не копируются: ответ должен выглядеть как полноценный файл,
  // который можно перематывать, — иначе Safari откажется играть его с середины.
  const cache = await caches.open(AUDIO_CACHE);
  await cache.put(
    url,
    new Response(body, {
      status: 200,
      headers: {
        "Content-Type": body.type,
        "Content-Length": String(body.size),
        "Accept-Ranges": "bytes",
      },
    }),
  );

  const entry: OfflineTrack = { track, quality, bytes: body.size, savedAt: Date.now() };
  await run("readwrite", (store) => store.put(entry));

  onProgress?.(1);
  return entry;
}

export async function removeOffline(trackId: string): Promise<void> {
  if (!offlineSupported()) return;

  const cache = await caches.open(AUDIO_CACHE);

  // Ступень качества могла с тех пор смениться, поэтому удаляется всё, что сохранено для трека.
  for (const request of await cache.keys()) {
    if (request.url.includes(`/tracks/${trackId}/stream`)) await cache.delete(request);
  }

  await run("readwrite", (store) => store.delete(trackId));
}

/** Стирает всё скачанное — вызывается при выходе из учётной записи. */
export async function clearOffline(): Promise<void> {
  if (!offlineSupported()) return;

  await caches.delete(AUDIO_CACHE);
  await run("readwrite", (store) => store.clear());
}
