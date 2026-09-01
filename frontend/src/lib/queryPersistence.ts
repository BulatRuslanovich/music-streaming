// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { dehydrate, hydrate, type QueryClient } from "@tanstack/react-query";

const DATABASE = "caimack-query-cache";
const STORE = "snapshots";
const RECORD = "current";
const DATABASE_VERSION = 1;

const MAX_AGE_MS = 24 * 60 * 60 * 1000;

const WRITE_DEBOUNCE_MS = 1_000;

/**
 * Ключи, которые переживать перезагрузку не должны.
 *
 * Раньше здесь был обратный список — четыре ключа из двадцати четырёх, — и всё остальное
 * (страницы альбома и артиста, избранное, история, обзор библиотеки) при каждом заходе бралось
 * из сети заново. Список-исключение держит по умолчанию всё: сюда попадает только то, что
 * устаревает быстрее, чем успевает пригодиться, или опрашивается по таймеру.
 */
const VOLATILE_KEYS = new Set([
  "search",
  "searchTab",
  "libraryImport",
  "lastfmStatus",
  "adminUsers",
]);

// IndexedDB не упирается в мегабайтный лимит localStorage и не пишет из главного потока.
const MAX_BYTES = 8_000_000;

interface Snapshot {
  version: string;
  userId: string;
  savedAt: number;
  state: unknown;
}

function currentVersion(): string {
  return process.env.APP_VERSION ?? "0";
}

function openDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DATABASE, DATABASE_VERSION);
    request.onupgradeneeded = () => {
      if (!request.result.objectStoreNames.contains(STORE)) {
        request.result.createObjectStore(STORE);
      }
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

function withStore<T>(
  mode: IDBTransactionMode,
  action: (store: IDBObjectStore) => IDBRequest<T>,
): Promise<T> {
  return openDatabase().then(
    (database) =>
      new Promise<T>((resolve, reject) => {
        const transaction = database.transaction(STORE, mode);
        const request = action(transaction.objectStore(STORE));
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
        transaction.oncomplete = () => database.close();
      }),
  );
}

/**
 * Поднимает снимок кэша с прошлого визита.
 *
 * Чтение асинхронное, то есть данные приезжают на кадр-другой позже первого рендера. Против
 * сетевого round-trip на медленном канале это ничто, а hydrate из TanStack не затирает то, что
 * уже успело прийти свежим: он сравнивает dataUpdatedAt.
 */
export async function restoreQueryCache(client: QueryClient, userId: string): Promise<void> {
  try {
    const snapshot = await withStore<Snapshot | undefined>("readonly", (store) =>
      store.get(RECORD),
    );
    if (!snapshot) return;

    const expired = Date.now() - snapshot.savedAt > MAX_AGE_MS;

    if (snapshot.version !== currentVersion() || snapshot.userId !== userId || expired) {
      dropQueryCache();
      return;
    }

    hydrate(client, snapshot.state);
  } catch {
    dropQueryCache();
  }
}

export function persistQueryCache(client: QueryClient, userId: string): () => void {
  let timer: number | null = null;

  const write = () => {
    timer = null;

    try {
      const state = dehydrate(client, {
        shouldDehydrateQuery: (query) =>
          query.state.status === "success" && !VOLATILE_KEYS.has(String(query.queryKey[0])),
      });

      const snapshot: Snapshot = {
        version: currentVersion(),
        userId,
        savedAt: Date.now(),
        state,
      };

      // Оценка объёма до записи: снимок кладётся целиком, и раздувать его без предела незачем.
      if (JSON.stringify(state).length > MAX_BYTES) return;

      void withStore("readwrite", (store) => store.put(snapshot, RECORD)).catch(() => {});
    } catch {}
  };

  const unsubscribe = client.getQueryCache().subscribe(() => {
    if (timer !== null) return;
    timer = window.setTimeout(write, WRITE_DEBOUNCE_MS);
  });

  return () => {
    if (timer !== null) window.clearTimeout(timer);
    unsubscribe();
  };
}

export function dropQueryCache(): void {
  void withStore("readwrite", (store) => store.delete(RECORD)).catch(() => {});

  // Снимок из старой версии лежал в localStorage — подчищаем за собой при первом же случае.
  try {
    window.localStorage.removeItem("music-streaming.query-cache");
  } catch {}
}
