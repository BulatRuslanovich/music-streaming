"use client";

import { useCallback, useEffect, useState } from "react";
import { dropQuery, readQuery, writeQuery } from "@/lib/queryCache";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";

interface ApiQuery<T> {
  data: T | null;
  error: string | null;
  loading: boolean;
  reload: () => void;
  patch: (updater: (current: T) => T) => void;
}

interface Settled<T> {
  key: string;
  data: T | null;
  error: string | null;
}

export function useApi<T>(
  loader: () => Promise<T>,
  deps: unknown[] = [],
  cacheKey?: string,
): ApiQuery<T> {
  const { notifyError } = useToast();
  const t = useT();
  const [reloadToken, setReloadToken] = useState(0);

  const depsKey = JSON.stringify(deps);
  const key = `${reloadToken}:${depsKey}`;
  const entryKey = cacheKey ? `${cacheKey}:${depsKey}` : null;

  const [settled, setSettled] = useState<Settled<T>>({ key: "", data: null, error: null });

  if (settled.key !== key && entryKey) {
    const cached = readQuery<T>(entryKey);
    if (cached !== null) {
      setSettled({ key, data: cached, error: null });
    }
  }

  useEffect(() => {
    let active = true;

    loader()
      .then((result) => {
        if (!active) return;

        if (entryKey) writeQuery(entryKey, result);
        setSettled({ key, data: result, error: null });
      })
      .catch((reason: unknown) => {
        if (!active) return;

        setSettled((current) => {
          const keepsStaleData = current.key === key && current.data !== null;

          return {
            key,
            data: keepsStaleData ? current.data : null,
            error: keepsStaleData
              ? null
              : reason instanceof Error
                ? reason.message
                : t("error.load"),
          };
        });

        notifyError(reason, t("error.load"));
      });

    return () => {
      active = false;
    };
  }, [key, entryKey, notifyError, t]);

  const reload = useCallback(() => {
    if (entryKey) dropQuery(entryKey);
    setReloadToken((token) => token + 1);
  }, [entryKey]);

  const patch = useCallback(
    (updater: (current: T) => T) => {
      setSettled((current) => {
        if (current.data === null) return current;

        const patched = updater(current.data);
        if (entryKey) writeQuery(entryKey, patched);

        return { ...current, data: patched };
      });
    },
    [entryKey],
  );

  return {
    data: settled.data,
    error: settled.key === key ? settled.error : null,
    loading: settled.key !== key,
    reload,
    patch,
  };
}
