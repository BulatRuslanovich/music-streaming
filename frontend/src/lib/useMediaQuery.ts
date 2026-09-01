// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useCallback, useSyncExternalStore } from "react";

/**
 * Медиазапрос как состояние. Нужен там, где от ширины зависит не оформление, а разметка:
 * свёрнутый сайдбар не просто уже — в нём нет подписей, и одним CSS этого не выразить.
 *
 * На сервере всегда `false`: гидрация поправит результат первым же эффектом, а обратный
 * дефолт давал бы вспышку свёрнутого сайдбара на широком экране.
 */
export function useMediaQuery(query: string): boolean {
  const subscribe = useCallback(
    (listener: () => void) => {
      const media = window.matchMedia(query);
      media.addEventListener("change", listener);
      return () => media.removeEventListener("change", listener);
    },
    [query],
  );

  return useSyncExternalStore(
    subscribe,
    () => window.matchMedia(query).matches,
    () => false,
  );
}
