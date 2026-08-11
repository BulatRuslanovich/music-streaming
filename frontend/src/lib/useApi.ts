"use client";

import { useCallback, useEffect, useState } from "react";
import { useT } from "@/contexts/I18nContext";
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
  const t = useT();
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
            error: reason instanceof Error ? reason.message : t("error.load"),
          });
          notifyError(reason, t("error.load"));
        }
      });

    return () => {
      active = false;
    };
  }, [key, notifyError, t]);

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
