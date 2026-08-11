"use client";

import { useCallback, useEffect, useState } from "react";
import { useToast } from "@/contexts/ToastContext";

interface ApiQuery<T> {
  data: T | null;
  error: string | null;
  loading: boolean;
  reload: () => void;
  patch: (updater: (current: T) => T) => void;
}

export function useApi<T>(loader: () => Promise<T>, deps: unknown[] = []): ApiQuery<T> {
  const { notifyError } = useToast();
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
          notifyError(reason, "Failed to load data.");
        }
      });

    return () => {
      active = false;
    };
  }, [key, notifyError]);

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
