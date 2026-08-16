"use client";

import { QueryCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import { ApiError } from "@/lib/http";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";

/** Столько же, сколько жил прежний свой кэш: пять минут данные считаются свежими. */
const STALE_MS = 5 * 60 * 1000;

export function QueryProvider({ children }: { children: ReactNode }) {
  const t = useT();
  const { notifyError } = useToast();

  const [client] = useState(
    () =>
      new QueryClient({
        // Ошибку показывает кэш, а не каждая страница по отдельности — как было раньше в useApi.
        queryCache: new QueryCache({
          onError: (error) => notifyError(error, t("error.load")),
        }),
        defaultOptions: {
          queries: {
            staleTime: STALE_MS,
            /*
             * Повтор только тогда, когда виновата связь или сервер. На 4xx повторять нечего:
             * второй такой же запрос вернёт тот же отказ, только пользователь узнает о нём позже.
             */
            retry: (attempt, error) =>
              attempt < 1 && (!(error instanceof ApiError) || error.status >= 500),
            /* Возврат в вкладку не повод перезапрашивать библиотеку: музыка играет часами. */
            refetchOnWindowFocus: false,
          },
        },
      }),
  );

  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
