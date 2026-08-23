// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { QueryCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useEffect, useState, type ReactNode } from "react";
import { ApiError } from "@/lib/http";
import { persistQueryCache, restoreQueryCache } from "@/lib/queryPersistence";
import { readSessionHint } from "@/lib/sessionHint";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";

const STALE_MS = 5 * 60 * 1000;

export function QueryProvider({ children }: { children: ReactNode }) {
  const t = useT();
  const { notifyError } = useToast();

  const [client] = useState(
    () =>
      new QueryClient({
        queryCache: new QueryCache({
          onError: (error) => notifyError(error, t("error.load")),
        }),
        defaultOptions: {
          queries: {
            staleTime: STALE_MS,
            retry: (attempt, error) =>
              attempt < 1 && (!(error instanceof ApiError) || error.status >= 500),
            refetchOnWindowFocus: false,
          },
        },
      }),
  );

  // Пользователь берётся из cookie-подсказки, а не из AuthContext: тот живёт ниже по дереву,
  // и ждать его значило бы отложить восстановление до конца первого запроса к серверу.
  useEffect(() => {
    const hint = readSessionHint();
    if (!hint) return;

    restoreQueryCache(client, hint.id);
    return persistQueryCache(client, hint.id);
  }, [client]);

  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
