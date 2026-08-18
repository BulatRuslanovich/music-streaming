import { mediaUrl } from "@/lib/media";
import type { AudioQuality, Track } from "@/lib/types";

const DATABASE = "caimack-offline";
const STORE = "tracks";
const VERSION = 1;

const AUDIO_CACHE = "caimack-audio-v1";

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
  const contentType = response.headers.get("Content-Type") ?? "audio/mpeg";

  let received = 0;

  const counted = response.body.pipeThrough(
    new TransformStream<Uint8Array, Uint8Array>({
      transform(chunk, controller) {
        received += chunk.length;
        if (expected > 0) onProgress?.(Math.min(1, received / expected));
        controller.enqueue(chunk);
      },
    }),
  );

  const cache = await caches.open(AUDIO_CACHE);
  await cache.put(
    url,
    new Response(counted, {
      status: 200,
      headers: {
        "Content-Type": contentType,
        "Content-Length": String(expected),
        "Accept-Ranges": "bytes",
      },
    }),
  );

  const entry: OfflineTrack = { track, quality, bytes: received, savedAt: Date.now() };
  await run("readwrite", (store) => store.put(entry));

  onProgress?.(1);
  return entry;
}

export async function removeOffline(trackId: string): Promise<void> {
  if (!offlineSupported()) return;

  const cache = await caches.open(AUDIO_CACHE);

  for (const request of await cache.keys()) {
    if (request.url.includes(`/tracks/${trackId}/stream`)) await cache.delete(request);
  }

  await run("readwrite", (store) => store.delete(trackId));
}

export async function clearOffline(): Promise<void> {
  if (!offlineSupported()) return;

  await caches.delete(AUDIO_CACHE);
  await run("readwrite", (store) => store.clear());
}
