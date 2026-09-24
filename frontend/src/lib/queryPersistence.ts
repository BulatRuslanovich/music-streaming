// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { dehydrate, hydrate, type DehydratedState, type QueryClient } from "@tanstack/react-query";

const DATABASE = "caimack-query-cache";
const STORE = "snapshots";
const RECORD = "current";
const DATABASE_VERSION = 1;

const MAX_AGE_MS = 24 * 60 * 60 * 1000;

/**
 * Пауза перед записью снимка.
 *
 * Подписка на кэш срабатывает на любое событие — добавление наблюдателя, начало и конец
 * загрузки, каждый setQueryData. На бесконечной прокрутке или при инвалидации истории после
 * каждого трека это непрерывный поток, и весь снимок пересобирается заново на каждом окне.
 * Секунда означала, что это происходит всю сессию; на пяти оно случается, когда кэш
 * действительно устоялся.
 */
const WRITE_DEBOUNCE_MS = 5_000;

/**
 * Сколько запросов и сколько страниц внутри бесконечного запроса попадает в снимок.
 *
 * Раньше объём ограничивался постфактум: снимок целиком прогонялся через JSON.stringify ради
 * одной только длины строки — до восьми мегабайт сериализации на главном потоке, результат
 * которой выбрасывался. Пределы по числу записей дают то же самое, но их видно заранее и они
 * ничего не стоят. Отбираются свежайшие: их и попросят первыми на следующем заходе.
 */
const MAX_QUERIES = 120;
const MAX_INFINITE_PAGES = 3;

/**
 * Ключи, которые переживать перезагрузку не должны.
 *
 * Список разрешающий, а не запрещающий. Обратный — четыре ключа из двадцати четырёх — означал бы, что всё остальное
 * (страницы альбома и артиста, избранное, история, обзор библиотеки) при каждом заходе бралось
 * из сети заново. Список-исключение держит по умолчанию всё: сюда попадает только то, что
 * устаревает быстрее, чем успевает пригодиться, или опрашивается по таймеру.
 */
// Сверять руками с queries.ts: сюда идёт queryKey[0], и промах именем не ломается, а тихо
// перестаёт исключать. Так «searchTab» не соответствовал ничему (ключ поиска — "search", он
// уже в списке), а «lastfmStatus» промахивался мимо "lastfm", и статус переживал перезагрузку.
const VOLATILE_KEYS = new Set(["search", "libraryImport", "lastfm", "adminUsers"]);

type DehydratedQuery = DehydratedState["queries"][number];

function isInfiniteData(data: unknown): data is { pages: unknown[]; pageParams: unknown[] } {
  return (
    typeof data === "object" &&
    data !== null &&
    Array.isArray((data as { pages?: unknown }).pages) &&
    Array.isArray((data as { pageParams?: unknown }).pageParams)
  );
}

/** Десяток страниц бесконечной прокрутки восстанавливать незачем: листают её заново сверху. */
function trimPages(query: DehydratedQuery): DehydratedQuery {
  const data = query.state.data;
  if (!isInfiniteData(data) || data.pages.length <= MAX_INFINITE_PAGES) return query;

  return {
    ...query,
    state: {
      ...query.state,
      data: {
        pages: data.pages.slice(0, MAX_INFINITE_PAGES),
        pageParams: data.pageParams.slice(0, MAX_INFINITE_PAGES),
      },
    },
  };
}

function trim(state: DehydratedState): DehydratedState {
  const queries = [...state.queries]
    .sort((left, right) => (right.state.dataUpdatedAt ?? 0) - (left.state.dataUpdatedAt ?? 0))
    .slice(0, MAX_QUERIES)
    .map(trimPages);

  return { ...state, queries };
}

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
        state: trim(state),
      };

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
