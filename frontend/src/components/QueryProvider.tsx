// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/lib/useToast";

const STALE_MS = 5 * 60 * 1000;

// Держим ответы сутки. При дефолтном gcTime — пять минут, ровно как staleTime — возврат на
// страницу спустя эти пять минут был бы холодным запросом, а не показом устаревших данных с
// фоновым обновлением. На медленном канале это и есть разница между «мгновенно» и «ждём».
const GC_MS = 24 * 60 * 60 * 1000;

export function QueryProvider({ children }: { children: ReactNode }) {
  const t = useT();
  const { notifyError } = useToast();

  const [client] = useState(
    () =>
      new QueryClient({
        queryCache: new QueryCache({
          onError: (error) => notifyError(error, t("error.load")),
        }),
        // Ошибку любого действия показывает это место, а не каждая кнопка в своём catch.
        mutationCache: new MutationCache({ onError: (error) => notifyError(error) }),
        defaultOptions: {
          queries: {
            staleTime: STALE_MS,
            gcTime: GC_MS,
            // Повторы уже делает fetchWithRetry в http.ts (две попытки на шлюзовые статусы).
            // Ещё один слой поверх давал до шести round-trip на один упавший GET — на канале
            // с высоким пингом это секунды ожидания вместо честной ошибки.
            retry: false,
            refetchOnWindowFocus: false,
            // Вернулась связь — самый подходящий момент обновить то, что показано.
            refetchOnReconnect: true,
            // Есть что показать из кэша — показываем, даже если сети нет.
            networkMode: "offlineFirst",
          },
          // Без сети действие должно упасть с ошибкой, а не встать на паузу: иначе кнопка висела
          // бы занятой до возврата связи и потом срабатывала невпопад.
          mutations: { networkMode: "always" },
        },
      }),
  );

  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
