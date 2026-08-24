// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { dehydrate, hydrate, type QueryClient } from "@tanstack/react-query";

const STORAGE_KEY = "music-streaming.query-cache";

const MAX_AGE_MS = 24 * 60 * 60 * 1000;

const WRITE_DEBOUNCE_MS = 1_000;

const PERSISTED_KEYS = new Set(["homeFeed", "homeMix", "playlists", "genres"]);

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
  try {
    window.localStorage.removeItem(STORAGE_KEY);
  } catch {}
}
