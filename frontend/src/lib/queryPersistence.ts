// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { dehydrate, hydrate, type QueryClient } from "@tanstack/react-query";

const STORAGE_KEY = "music-streaming.query-cache";

const MAX_AGE_MS = 24 * 60 * 60 * 1000;

const WRITE_DEBOUNCE_MS = 1_000;

/**
 * Кладём в localStorage только дешёвые ответы, с которых начинается экран. Списки треков
 * с постраничностью сюда не идут: они и крупные, и устаревают заметнее всего.
 */
const PERSISTED_KEYS = new Set(["homeFeed", "homeMix", "playlists", "genres"]);

/** Пятимегабайтную квоту делить не с кем, но раздувать запись на всю библиотеку незачем. */
const MAX_BYTES = 1_000_000;

interface Snapshot {
  version: string;
  userId: string;
  savedAt: number;
  state: unknown;
}

function currentVersion(): string {
  return process.env.APP_VERSION ?? "0";
}

/**
 * Кэш переживает перезагрузку, так что домашняя лента рисуется мгновенно, а сеть только
 * подтверждает её в фоне (staleTime всё равно пометит данные несвежими и запустит рефетч).
 *
 * Снимок привязан к id пользователя из cookie-подсказки: без этой проверки после смены
 * аккаунта на экране мог бы мелькнуть чужой список.
 */
export function restoreQueryCache(client: QueryClient, userId: string): void {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return;

    const snapshot = JSON.parse(raw) as Snapshot;
    const expired = Date.now() - snapshot.savedAt > MAX_AGE_MS;

    if (snapshot.version !== currentVersion() || snapshot.userId !== userId || expired) {
      window.localStorage.removeItem(STORAGE_KEY);
      return;
    }

    hydrate(client, snapshot.state);
  } catch {
    dropQueryCache();
  }
}

/** Возвращает отписку — подписка живёт столько же, сколько сам QueryClient. */
export function persistQueryCache(client: QueryClient, userId: string): () => void {
  let timer: number | null = null;

  const write = () => {
    timer = null;

    try {
      const state = dehydrate(client, {
        shouldDehydrateQuery: (query) =>
          query.state.status === "success" && PERSISTED_KEYS.has(String(query.queryKey[0])),
      });

      const payload = JSON.stringify({
        version: currentVersion(),
        userId,
        savedAt: Date.now(),
        state,
      } satisfies Snapshot);

      if (payload.length > MAX_BYTES) return;

      window.localStorage.setItem(STORAGE_KEY, payload);
    } catch {
      // Приватный режим, переполненная квота — персистентность необязательная, молча живём без неё.
    }
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
  try {
    window.localStorage.removeItem(STORAGE_KEY);
  } catch {}
}
