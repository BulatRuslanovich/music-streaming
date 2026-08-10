"use client";

import { useCallback, useEffect, useState } from "react";

interface ApiQuery<T> {
  data: T | null;
  error: string | null;
  loading: boolean;
  /** Re-runs the request, e.g. after a mutation. */
  reload: () => void;
  /** Updates the cached value locally, for optimistic UI. */
  patch: (updater: (current: T) => T) => void;
}

/**
 * Minimal data-loading hook: runs `loader` on mount and whenever `deps` change.
 *
 * `loading` is derived rather than stored — the settled result carries the key it was fetched
 * for, so a key that has not been resolved yet *is* the loading state. That avoids a
 * set-state-in-effect cascade and, as a bonus, keeps the previous data visible while a new
 * request is in flight instead of blanking the page on every dependency change.
 */
export function useApi<T>(loader: () => Promise<T>, deps: unknown[] = []): ApiQuery<T> {
  const [reloadToken, setReloadToken] = useState(0);

  const key = `${reloadToken}:${JSON.stringify(deps)}`;

  const [settled, setSettled] = useState<{ key: string; data: T | null; error: string | null }>({
    key: "",
    data: null,
    error: null,
  });

  useEffect(() => {
    let active = true;

    loader()
      .then((result) => {
        if (active) setSettled({ key, data: result, error: null });
      })
      .catch((reason: unknown) => {
        if (active) {
          setSettled({
            key,
            data: null,
            error: reason instanceof Error ? reason.message : "Failed to load data.",
          });
        }
      });

    // Dropping the stale result is what makes out-of-order responses harmless.
    return () => {
      active = false;
    };
    // `loader` is an inline closure that changes every render; `key` is the real dependency.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);

  const reload = useCallback(() => setReloadToken((token) => token + 1), []);

  const patch = useCallback((updater: (current: T) => T) => {
    setSettled((current) =>
      current.data === null ? current : { ...current, data: updater(current.data) },
    );
  }, []);

  return {
    data: settled.data,
    error: settled.key === key ? settled.error : null,
    loading: settled.key !== key,
    reload,
    patch,
  };
}
